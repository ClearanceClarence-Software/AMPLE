using System.Diagnostics;
using Ample.Core;

namespace Ample.Services;

/// <summary>
/// Apache httpd, run from bin\apache with its config in etc\apache. The root is handed to httpd on the command
/// line as the AMPLE_ROOT define, so the config files never contain the install path and the folder can move.
/// </summary>
/// <param name="host">AMPLE: paths, settings and the log.</param>
public sealed class ApacheService(IServiceHost host) : ServiceDriver(host)
{
    public override string Id => "apache";
    public override string DisplayName => "Web server";
    public override string Tag => "apache";
    public override string FolderPath => Paths.Apache;
    protected override string[] ProcessNames => ["httpd"];

    public override string? EngineLabel =>
        AmplePaths.FileVersion(Paths.HttpdExe) is { } version ? $"Apache {version}" : null;

    public override string? ServiceName => "AMPLE-Apache";

    public override (string Exe, string Args)? ServiceInstallCommand =>
        (Paths.HttpdExe, $"-k install -n \"{ServiceName}\" {RootArguments(Paths)}");

    public override (string Exe, string Args)? ServiceUninstallCommand =>
        (Paths.HttpdExe, $"-k uninstall -n \"{ServiceName}\"");

    public override IReadOnlyList<int> ConfiguredPorts => [Settings.Ports.Http, Settings.Ports.Https];

    public override IEnumerable<(string Label, string Path)> ConfigFiles =>
    [
        ("httpd.conf", Paths.HttpdConf),
        ("projects.conf", Paths.ProjectsConf),
        ("Custom config folder", Paths.EtcApacheCustom),
    ];

    public override IEnumerable<(string Label, string Path)> LogFiles =>
    [
        ("error.log", Path.Combine(Paths.LogsApache, "error.log")),
        ("access.log", Path.Combine(Paths.LogsApache, "access.log")),
        ("PHP error log", Path.Combine(Paths.LogsPhp, "php_errors.log")),
    ];

    /// <summary>
    /// The arguments that point httpd at this stack: its server root, its config file and the AMPLE_ROOT define
    /// every path in the config is built from.
    /// </summary>
    /// <param name="paths">The stack.</param>
    /// <returns>The -d, -f and -C arguments, quoted.</returns>
    public static string RootArguments(AmplePaths paths) =>
        $"-d \"{AmplePaths.ConfigPath(paths.Apache)}\" -f \"{AmplePaths.ConfigPath(paths.HttpdConf)}\" " +
        $"-C \"Define AMPLE_ROOT {paths.RootForConfig}\"";

    public override async Task StartAsync()
    {
        if (!File.Exists(Paths.HttpdExe))
        {
            Host.Write(Tag, @"httpd.exe not found in bin\apache\bin", LogLevel.Error);
            return;
        }

        if (!await PassesConfigTestAsync())
        {
            return;
        }

        Host.Write(Tag, "Starting the web server…");
        string arguments = RootArguments(Paths);
        Host.Debug(Tag, $"Executing \"{Paths.HttpdExe}\" {arguments}");
        using Process? process = Shell.StartHidden(Paths.HttpdExe, arguments, Paths.Root);
        if (process is null)
        {
            Host.Write(Tag, "Could not launch httpd.exe", LogLevel.Error);
            return;
        }

        // A bad config makes httpd exit within a second or two; a healthy server keeps running.
        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(3)))
        {
            Host.Write(Tag,
                $"The web server shut down unexpectedly (exit code {process.ExitCode}). Last lines of error.log:",
                LogLevel.Error);
            Host.TailLog(Tag, Path.Combine(Paths.LogsApache, "error.log"), 6);
        }
    }

    /// <summary>Runs httpd -t and logs what it finds, so a broken config is named before Apache starts.</summary>
    /// <returns>True when the config is valid, or when the test timed out (Apache then starts as usual).</returns>
    private async Task<bool> PassesConfigTestAsync()
    {
        Host.Debug(Tag, "Checking the Apache configuration (httpd -t)");
        Shell.RunResult result = await TestConfigAsync(Paths);
        if (result.TimedOut || result.ExitCode == 0)
        {
            return true;
        }

        Host.Write(Tag, "Apache configuration error, so the web server was not started:", LogLevel.Error);
        foreach (string line in ConfigTestProblems(result))
        {
            Host.Write(Tag, "  › " + line, LogLevel.Error);
        }
        return false;
    }

    /// <summary>Checks Apache's configuration with httpd -t without starting the server.</summary>
    /// <param name="paths">The stack whose httpd.exe and config are tested.</param>
    /// <returns>
    /// httpd's result: exit code 0 and "Syntax OK" when valid; -1 with the reason when httpd.exe is missing, could
    /// not start or ran past 20 seconds.
    /// </returns>
    public static async Task<Shell.RunResult> TestConfigAsync(AmplePaths paths)
    {
        if (!File.Exists(paths.HttpdExe))
        {
            return new Shell.RunResult(-1, "", @"httpd.exe not found in bin\apache\bin", false);
        }

        return await Shell.RunAsync(paths.HttpdExe, "-t " + RootArguments(paths), paths.Root, TimeSpan.FromSeconds(20));
    }

    /// <summary>The lines of a failed httpd -t that describe the problem.</summary>
    /// <param name="result">The test's result; httpd writes its findings to standard error.</param>
    /// <returns>The non-empty lines of both streams except "Syntax OK"; the exit code when there are none.</returns>
    public static List<string> ConfigTestProblems(Shell.RunResult result)
    {
        var problems = (result.Error + "\n" + result.Output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.Equals("Syntax OK", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (problems.Count == 0)
        {
            problems.Add($"httpd -t failed with exit code {result.ExitCode}.");
        }
        return problems;
    }

    public override async Task<bool> StopAsync()
    {
        IReadOnlyList<int> pids = await PidsAsync();
        Host.Write(Tag, $"Stopping the web server{PidText(pids)}");
        return await KillAllAsync();
    }
}
