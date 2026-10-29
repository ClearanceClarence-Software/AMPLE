using Ample.Core;

namespace Ample.Services;

/// <summary>Knows how to detect, start and stop one server of the stack.</summary>
/// <param name="host">AMPLE: paths, settings, stack state and the log.</param>
public abstract class ServiceDriver(IServiceHost host)
{
    /// <summary>AMPLE: paths, settings, stack state and the log.</summary>
    protected IServiceHost Host { get; } = host;

    /// <summary>The folders of the stack.</summary>
    protected AmplePaths Paths => Host.Paths;

    /// <summary>Live settings; ports are read on each use, so edits apply right away.</summary>
    protected AppSettings Settings => Host.Settings;

    /// <summary>A stable id used in settings and logs, e.g. "apache".</summary>
    public abstract string Id { get; }

    /// <summary>What the user sees: "Web server", "Database".</summary>
    public abstract string DisplayName { get; }

    /// <summary>What runs underneath: "Apache 2.4.66", "MariaDB 11.4.5"; null when the exe is missing.</summary>
    public abstract string? EngineLabel { get; }

    /// <summary>Short tag used in the log.</summary>
    public virtual string Tag => Id;

    /// <summary>The folder the component was unpacked into.</summary>
    public abstract string FolderPath { get; }

    /// <summary>True when the component is unpacked; an uninstalled service cannot start.</summary>
    public virtual bool IsInstalled => Directory.Exists(FolderPath);

    /// <summary>Windows service name, or null if the service can't run as one.</summary>
    public virtual string? ServiceName => null;

    /// <summary>The program and arguments that install <see cref="ServiceName"/>; null when not available.</summary>
    public virtual (string Exe, string Args)? ServiceInstallCommand => null;

    /// <summary>The program and arguments that remove <see cref="ServiceName"/>; null when not available.</summary>
    public virtual (string Exe, string Args)? ServiceUninstallCommand => null;

    /// <summary>Ports this service needs free.</summary>
    public virtual IReadOnlyList<int> ConfiguredPorts => [];

    /// <summary>When a graceful stop fails, AMPLE offers "Force stop".</summary>
    public virtual bool SupportsForceStop => false;

    /// <summary>Config files (label, full path). Missing files are hidden.</summary>
    public virtual IEnumerable<(string Label, string Path)> ConfigFiles => [];

    /// <summary>Log files or folders (label, full path). Missing ones are hidden.</summary>
    public virtual IEnumerable<(string Label, string Path)> LogFiles => [];

    /// <summary>Process names without ".exe" that the service runs as.</summary>
    protected abstract string[] ProcessNames { get; }

    /// <summary>PIDs that belong to this service. Runs on a background thread.</summary>
    /// <returns>The PIDs in ascending order; empty when the service is not running.</returns>
    public virtual IReadOnlyList<int> DetectPids() => ProcessScan.FindPids(ProcessNames, FolderPath);

    /// <summary>Starts the service and logs what happened; servers that exit right away are reported.</summary>
    /// <returns>A task that completes after the start attempt and any early-exit check, not when ready.</returns>
    public abstract Task StartAsync();

    /// <summary>Stops the service the normal way and logs the outcome.</summary>
    /// <returns>False if the service is still running afterwards.</returns>
    public abstract Task<bool> StopAsync();

    /// <summary>Ends every process tree of the service without a clean shutdown.</summary>
    /// <returns>A task that completes when the processes are gone or after 10 seconds of waiting.</returns>
    public virtual async Task ForceStopAsync()
    {
        await KillAllAsync();
    }

    // ── helpers ─────────────────────────────────────────────

    /// <summary>Runs <see cref="DetectPids"/> on a background thread.</summary>
    /// <returns>The service's PIDs; empty when it is not running.</returns>
    protected Task<IReadOnlyList<int>> PidsAsync() => Task.Run(DetectPids);

    /// <summary>"(PID: 2524, 27820)" with a leading space, ready to append to a log line.</summary>
    /// <param name="pids">The PIDs to list, in the order given.</param>
    /// <returns>The text with a leading space, or empty when there are none.</returns>
    public static string PidText(IReadOnlyCollection<int> pids) =>
        pids.Count == 0 ? "" : $" (PID: {string.Join(", ", pids)})";

    /// <summary>Ends the process tree of every PID of the service and waits for them to go.</summary>
    /// <returns>True when no process of the service is left within 10 seconds.</returns>
    protected async Task<bool> KillAllAsync()
    {
        IReadOnlyList<int> pids = await PidsAsync();
        foreach (int pid in pids)
        {
            Host.Debug(Tag, $"Ending process tree of PID {pid}");
            ProcessScan.KillTree(pid);
        }

        return await WaitUntilStoppedAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Polls the service's PIDs every 300 ms until none are left or the time runs out.</summary>
    /// <param name="timeout">How long to keep polling.</param>
    /// <returns>True when the service stopped in time; false when it is still running.</returns>
    protected async Task<bool> WaitUntilStoppedAsync(TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if ((await PidsAsync()).Count == 0)
            {
                return true;
            }

            await Task.Delay(300);
        }

        return false;
    }

    /// <summary>An http://localhost URL, leaving the port out when it is 80.</summary>
    /// <param name="port">TCP port the local server listens on.</param>
    /// <param name="path">Path after the host, starting with a slash.</param>
    /// <returns>The URL, e.g. "http://localhost/" or "http://localhost:8080/ledger/".</returns>
    public static string LocalUrl(int port, string path = "/") =>
        port == 80 ? $"http://localhost{path}" : $"http://localhost:{port}{path}";
}
