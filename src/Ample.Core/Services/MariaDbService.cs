using System.Diagnostics;
using Ample.Core;

namespace Ample.Services;

/// <summary>
/// MariaDB, run from bin\mariadb with etc\mariadb\my.ini and its data in data\mariadb. Stops with
/// mariadb-admin shutdown so InnoDB flushes its redo log; a hard kill is only offered as "Force stop" when the
/// clean shutdown fails.
/// </summary>
/// <param name="host">AMPLE: paths, settings, stack state and the log.</param>
public sealed class MariaDbService(IServiceHost host) : ServiceDriver(host)
{
    public override string Id => "mariadb";
    public override string DisplayName => "Database";
    public override string Tag => "mariadb";
    protected override string[] ProcessNames => ["mariadbd", "mysqld"];

    /// <summary>The version that runs: the stack's active one, else the newest installed; "" before any
    /// install.</summary>
    public string ActiveVersion =>
        Host.Stack.ActiveMariaDb ?? Paths.InstalledMariaDbVersions().FirstOrDefault() ?? "";

    /// <summary>bin\mariadb\&lt;active version&gt;.</summary>
    public override string FolderPath => Paths.MariaDb(ActiveVersion);

    /// <summary>bin\mariadb\&lt;active version&gt;\bin.</summary>
    private string Bin => Paths.MariaDbBin(ActiveVersion);

    /// <summary>True when more than one version is installed, so the server must be found by its own folder.</summary>
    public override bool IsInstalled => ActiveVersion.Length > 0 && ServerExe is not null;

    private string? ServerExe => AmplePaths.FirstExisting(
        Path.Combine(Bin, "mariadbd.exe"),
        Path.Combine(Bin, "mysqld.exe"));

    private string? AdminExe => AmplePaths.FirstExisting(
        Path.Combine(Bin, "mariadb-admin.exe"),
        Path.Combine(Bin, "mysqladmin.exe"));

    /// <summary>The error log my.ini points MariaDB at.</summary>
    public string ErrorLog => Path.Combine(Paths.LogsMariaDb, "error.log");

    public override string? EngineLabel =>
        AmplePaths.FileVersion(ServerExe) is { } version ? $"MariaDB {version}" : null;

    public override string? ServiceName => "AMPLE-MariaDB";

    public override (string Exe, string Args)? ServiceInstallCommand =>
        ServerExe is null ? null : (ServerExe, $"--install \"{ServiceName}\" --defaults-file=\"{Paths.MyIni}\"");

    public override (string Exe, string Args)? ServiceUninstallCommand =>
        ServerExe is null ? null : (ServerExe, $"--remove \"{ServiceName}\"");

    public override IReadOnlyList<int> ConfiguredPorts => [Settings.Ports.MariaDb];

    public override bool SupportsForceStop => true;

    public override IEnumerable<(string Label, string Path)> ConfigFiles => [("my.ini", Paths.MyIni)];

    public override IEnumerable<(string Label, string Path)> LogFiles =>
    [
        ("error.log", ErrorLog),
        ("Data folder", Paths.DataMariaDb(ActiveVersion)),
    ];

    public override async Task StartAsync()
    {
        string? serverExe = ServerExe;
        if (serverExe is null)
        {
            Host.Write(Tag, @"No mariadbd.exe in bin\mariadb\bin", LogLevel.Error);
            return;
        }

        if (!File.Exists(Paths.MyIni))
        {
            Host.Write(
                Tag,
                @"etc\mariadb\my.ini is missing; run Repair from Settings to write it again.",
                LogLevel.Error);
            return;
        }

        Host.Write(Tag, "Starting the database…");
        string arguments = $"--defaults-file=\"{Paths.MyIni}\" --standalone";
        Host.Debug(Tag, $"Executing \"{serverExe}\" {arguments}");
        using Process? process = Shell.StartHidden(serverExe, arguments, Paths.Root);
        if (process is null)
        {
            Host.Write(Tag, "Could not launch " + Path.GetFileName(serverExe), LogLevel.Error);
            return;
        }

        // A bad config or a locked data folder makes the server exit early; a healthy server keeps running.
        if (await Shell.ExitsWithinAsync(process, TimeSpan.FromSeconds(4)))
        {
            Host.Write(Tag,
                $"The database shut down unexpectedly (exit code {process.ExitCode}). Last lines of error.log:",
                LogLevel.Error);
            Host.TailLog(Tag, ErrorLog, 8);
        }
    }

    public override async Task<bool> StopAsync()
    {
        string? adminExe = AdminExe;
        if (adminExe is null)
        {
            Host.Write(Tag,
                @"No mariadb-admin.exe in bin\mariadb\bin, so a clean shutdown isn't possible.",
                LogLevel.Warn);
            return false;
        }

        IReadOnlyList<int> pids = await PidsAsync();
        Host.Write(Tag, $"Stopping the database{PidText(pids)} with a clean shutdown");

        Shell.RunResult result = await RunAdminShutdownAsync(adminExe);
        if (result.ExitCode != 0)
        {
            string reason = FirstLine(result.Error) ?? FirstLine(result.Output) ?? "unknown error";
            Host.Write(Tag, "Clean shutdown failed: " + reason, LogLevel.Error);
            Host.Write(
                Tag,
                "Use Force stop if it keeps failing (InnoDB will recover on the next start).",
                LogLevel.Warn);
            return false;
        }

        if (await WaitUntilStoppedAsync(TimeSpan.FromSeconds(30)))
        {
            Host.Write(Tag, "Database shut down cleanly.", LogLevel.Ok);
            return true;
        }

        Host.Write(Tag, "The database is still running after 30 s.", LogLevel.Warn);
        return false;
    }

    public override async Task ForceStopAsync()
    {
        Host.Write(
            Tag,
            "Force-stopping the database. InnoDB will run crash recovery on the next start.",
            LogLevel.Warn);
        await KillAllAsync();
    }

    /// <summary>Environment variables that sign the command-line clients in as root.</summary>
    /// <returns>MYSQL_PWD when a root password is set, so it never appears on a command line.</returns>
    public Dictionary<string, string> RootEnvironment()
    {
        var environment = new Dictionary<string, string>();
        if (Host.Stack.MariaDbRootPassword is { Length: > 0 } rootPassword)
        {
            environment["MYSQL_PWD"] = rootPassword;
        }
        return environment;
    }

    /// <summary>Client arguments that connect as root over TCP to the configured port on this machine.</summary>
    public string RootConnectionArguments => $"-u root -h 127.0.0.1 -P {Settings.Ports.MariaDb} --protocol=tcp";

    /// <summary>Asks the server to shut down as root over TCP on the configured port, with a 20-second limit.</summary>
    /// <param name="adminExe">Full path of mariadb-admin.exe or mysqladmin.exe.</param>
    /// <returns>The admin tool's exit code and output; exit code -1 when it could not start or timed out.</returns>
    private async Task<Shell.RunResult> RunAdminShutdownAsync(string adminExe)
    {
        string arguments = RootConnectionArguments + " shutdown";
        Host.Debug(Tag, $"Executing \"{adminExe}\" {arguments}");
        return await Shell.RunAsync(adminExe, arguments, Paths.Root, TimeSpan.FromSeconds(20), RootEnvironment());
    }

    /// <summary>The first non-blank line of a tool's output, trimmed.</summary>
    /// <param name="text">Captured standard output or error; may be empty.</param>
    /// <returns>The line, or null when the text has no non-blank line.</returns>
    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
