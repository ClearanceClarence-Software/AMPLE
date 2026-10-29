using Ample.Core;
using Ample.Services;

namespace Ample.App.ViewModels;

/// <summary>One service card on the Overview: its state, its Start/Stop button and its port conflict, if any.</summary>
public sealed class ServiceViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private IReadOnlyList<int> _pids = [];
    private bool _isBusy;
    private bool _forceStopPending;
    private string? _conflictText;
    private int _conflictPort;

    /// <summary>Wraps a driver.</summary>
    /// <param name="driver">The service.</param>
    /// <param name="main">AMPLE, for the log and the port table.</param>
    public ServiceViewModel(ServiceDriver driver, MainViewModel main)
    {
        Driver = driver;
        _main = main;
        ActionCommand = new RelayCommand(RunActionAsync, () => Driver.IsInstalled && !IsBusy);
        ForceStopCommand = new RelayCommand(ForceStopAsync, () => ForceStopPending && !IsBusy);
        FixPortCommand = new RelayCommand(FixPortAsync, () => HasConflict && !IsBusy);
        ConfigFiles = Links(driver.ConfigFiles);
        LogFiles = Links(driver.LogFiles);
    }

    /// <summary>The service.</summary>
    public ServiceDriver Driver { get; }

    /// <summary>"Web server", "Database".</summary>
    public string Name => Driver.DisplayName;

    /// <summary>"Apache 2.4.66"; "Not installed" when the exe is missing.</summary>
    public string Engine => Driver.EngineLabel ?? "Not installed";

    /// <summary>True when the component's folder exists.</summary>
    public bool IsInstalled => Driver.IsInstalled;

    /// <summary>The config files for the card's menu.</summary>
    public IReadOnlyList<FileLink> ConfigFiles { get; }

    /// <summary>The log files for the card's menu.</summary>
    public IReadOnlyList<FileLink> LogFiles { get; }

    /// <summary>Start or Stop, whichever applies.</summary>
    public RelayCommand ActionCommand { get; }

    /// <summary>Ends the processes after a clean stop failed.</summary>
    public RelayCommand ForceStopCommand { get; }

    /// <summary>Moves the service to a free port and starts it.</summary>
    public RelayCommand FixPortCommand { get; }

    /// <summary>The PIDs seen at the last refresh; empty when stopped.</summary>
    public IReadOnlyList<int> Pids
    {
        get => _pids;
        private set
        {
            if (SetField(ref _pids, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True while a start, stop or fix runs.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True after a clean stop failed, so the card offers Force stop.</summary>
    public bool ForceStopPending
    {
        get => _forceStopPending;
        private set
        {
            if (SetField(ref _forceStopPending, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>What blocks the port, for the card's notice; null when the ports are free or the service
    /// runs.</summary>
    public string? ConflictText
    {
        get => _conflictText;
        private set
        {
            if (SetField(ref _conflictText, value))
            {
                RaiseState();
            }
        }
    }

    /// <summary>True when the card shows a port conflict notice.</summary>
    public bool HasConflict => ConflictText is not null;

    /// <summary>The port AMPLE would move to, for the fix button.</summary>
    public int SuggestedPort => _conflictPort;

    /// <summary>"Switch to port 8080".</summary>
    public string FixLabel => $"Switch to port {_conflictPort}";

    /// <summary>True when at least one process of the service runs.</summary>
    public bool IsRunning => Pids.Count > 0;

    /// <summary>"Running", "Stopped", "Needs attention" or "Starting…" for the pill.</summary>
    public string StatusLabel
    {
        get
        {
            if (IsBusy)
            {
                return IsRunning ? "Stopping…" : "Starting…";
            }
            if (HasConflict)
            {
                return "Needs attention";
            }
            return IsRunning ? "Running" : "Stopped";
        }
    }

    /// <summary>The pill's style class: running, attention or stopped.</summary>
    public string PillClass => IsRunning && !IsBusy ? "running" : HasConflict ? "attention" : "stopped";

    /// <summary>True when the pill shows the running style.</summary>
    public bool ShowRunningPill => PillClass == "running";

    /// <summary>True when the pill shows the attention style.</summary>
    public bool ShowAttentionPill => PillClass == "attention";

    /// <summary>"Ports 80 · 443" or "Port 3306", from the settings.</summary>
    public string PortsText
    {
        get
        {
            IReadOnlyList<int> ports = Driver.ConfiguredPorts;
            return (ports.Count == 1 ? "Port " : "Ports ") + string.Join(" · ", ports);
        }
    }

    /// <summary>"Stop" when running, "Start" otherwise.</summary>
    public string ActionLabel => IsRunning ? "Stop" : "Start";

    /// <summary>Tells the bindings that everything derived from the state changed.</summary>
    private void RaiseState()
    {
        string[] properties =
        [
            nameof(IsRunning), nameof(StatusLabel), nameof(PillClass), nameof(ShowRunningPill),
            nameof(ShowAttentionPill), nameof(ActionLabel), nameof(HasConflict), nameof(FixLabel),
            nameof(SuggestedPort), nameof(PortsText), nameof(Engine), nameof(IsInstalled),
        ];
        foreach (string property in properties)
        {
            OnPropertyChanged(property);
        }
        ActionCommand.RaiseCanExecuteChanged();
        ForceStopCommand.RaiseCanExecuteChanged();
        FixPortCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Stores the PIDs from a refresh and clears stale state.</summary>
    /// <param name="pids">The PIDs found, ascending.</param>
    internal void Apply(IReadOnlyList<int> pids)
    {
        bool wasRunning = IsRunning;
        Pids = pids;
        if (IsRunning)
        {
            ForceStopPending = false;
            ConflictText = null;
        }
        if (wasRunning != IsRunning)
        {
            _main.NoteStateChanged();
        }
    }

    /// <summary>Starts the service when it is stopped; used by Start all and the auto start.</summary>
    public async Task StartIfStoppedAsync()
    {
        if (!IsRunning && !IsBusy && Driver.IsInstalled)
        {
            await StartAsync();
        }
    }

    /// <summary>Stops the service when it runs; used by Stop all and the shutdown.</summary>
    public async Task StopIfRunningAsync()
    {
        if (IsRunning && !IsBusy)
        {
            await StopAsync();
        }
    }

    /// <summary>Stops and starts the service when it runs, after a config change.</summary>
    public async Task RestartIfRunningAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        await StopAsync();
        if (!IsRunning)
        {
            await StartAsync();
        }
    }

    private async Task RunActionAsync()
    {
        if (IsRunning)
        {
            await StopAsync();
        }
        else
        {
            await StartAsync();
        }
    }

    private async Task StartAsync()
    {
        IsBusy = true;
        try
        {
            if (await FindConflictAsync() is { } conflict)
            {
                ConflictText = conflict.Text;
                _conflictPort = conflict.FreePort;
                _main.Write(Driver.Tag, conflict.Text, LogLevel.Warn);
                return;
            }

            ConflictText = null;
            await Driver.StartAsync();
            await _main.RefreshAsync(quiet: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopAsync()
    {
        IsBusy = true;
        try
        {
            bool stopped = await Driver.StopAsync();
            await _main.RefreshAsync(quiet: true);
            ForceStopPending = !stopped && IsRunning && Driver.SupportsForceStop;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ForceStopAsync()
    {
        IsBusy = true;
        try
        {
            await Driver.ForceStopAsync();
            await _main.RefreshAsync(quiet: true);
            ForceStopPending = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Moves the blocked port to the suggested free one, rewrites the configs and starts the
    /// service.</summary>
    private async Task FixPortAsync()
    {
        int blocked = Driver.ConfiguredPorts
            .FirstOrDefault(port => ConflictText!.Contains($"Port {port} ", StringComparison.Ordinal));
        if (blocked == 0)
        {
            blocked = Driver.ConfiguredPorts[0];
        }

        await _main.ChangePortAsync(Driver, blocked, _conflictPort);
        ConflictText = null;
        await StartAsync();
    }

    /// <summary>Looks for another program on the service's ports.</summary>
    /// <returns>The notice text and a free port to offer; null when every port is free.</returns>
    private async Task<(string Text, int FreePort)?> FindConflictAsync()
    {
        List<Listener> listeners = await Task.Run(PortTable.ListAll);
        var ownPids = new HashSet<int>(await Task.Run(() => Driver.DetectPids()));
        foreach (int port in Driver.ConfiguredPorts)
        {
            int? owner = PortTable.OwnerOf(port, listeners);
            if (owner is null || ownPids.Contains(owner.Value))
            {
                continue;
            }

            string program = ProcessScan.NameOf(owner.Value) ?? $"PID {owner}";
            int freePort = FreePortNear(port, listeners);
            return ($"Port {port} is in use by {program} on your computer.", freePort);
        }
        return null;
    }

    /// <summary>The usual alternative for a well-known port, or the next free port above it.</summary>
    private static int FreePortNear(int port, List<Listener> listeners)
    {
        int candidate = port switch
        {
            80 => 8080,
            443 => 8443,
            3306 => 3307,
            _ => port + 1,
        };
        while (PortTable.OwnerOf(candidate, listeners) is not null)
        {
            candidate++;
        }
        return candidate;
    }

    private List<FileLink> Links(IEnumerable<(string Label, string Path)> entries) =>
        entries.Select(entry => new FileLink(entry.Label, entry.Path, () => _main.Settings.Editor)).ToList();
}
