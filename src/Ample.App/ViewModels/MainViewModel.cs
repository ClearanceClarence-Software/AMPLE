using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Services;
using Ample.Stack;
using Avalonia.Threading;

namespace Ample.App.ViewModels;

/// <summary>
/// The shell: the stack being managed, its services, the log, the pages and the navigation between them. Also the
/// host every driver and the installer talk to.
/// </summary>
public sealed class MainViewModel : ObservableObject, IServiceHost, IDisposable
{
    private readonly DispatcherTimer _pollTimer;
    private readonly object _logFileLock = new();
    private ObservableObject? _currentPage;
    private bool _isRefreshing;

    /// <summary>Builds the shell for the stack the settings point at.</summary>
    /// <param name="settings">The loaded settings; kept by reference and saved by the pages.</param>
    /// <param name="rootOverride">
    /// A folder from the command line that wins over the settings and the exe's folder; null for neither.
    /// </param>
    public MainViewModel(AppSettings settings, string? rootOverride = null)
    {
        Settings = settings;
        Paths = AmplePaths.Resolve(rootOverride ?? settings.Root);
        Stack = StackState.Load(Paths.StackFile);
        Installer = new StackInstaller(this);

        Apache = new ServiceViewModel(new ApacheService(this), this);
        MariaDb = new ServiceViewModel(new MariaDbService(this), this);
        Services = [Apache, MariaDb];

        // The pages pick these commands up in their constructors, so they exist first. Navigation reads the page
        // properties lazily, at click time.
        ShowOverviewCommand = new RelayCommand(() => Navigate(Overview!));
        ShowProjectsCommand = new RelayCommand(() => Navigate(ProjectsPage!));
        ShowVersionsCommand = new RelayCommand(() =>
        {
            Navigate(VersionsPage!);
            _ = RunLoggedAsync(VersionsPage!.LoadAsync);
        });
        ShowLogCommand = new RelayCommand(() => Navigate(LogPage!));
        ShowSettingsCommand = new RelayCommand(() => Navigate(SettingsPage!));
        StartAllCommand = new RelayCommand(StartAllAsync, () => !IsSetupNeeded);
        StopAllCommand = new RelayCommand(StopAllAsync, () => !IsSetupNeeded);
        OpenLocalhostCommand = new RelayCommand(
            () => Shell.OpenUrl(ServiceDriver.LocalUrl(Settings.Ports.Http), Settings.Browser));

        Overview = new OverviewViewModel(this);
        ProjectsPage = new ProjectsViewModel(this);
        VersionsPage = new VersionsViewModel(this);
        LogPage = new LogViewModel(this);
        SettingsPage = new SettingsViewModel(this);

        RelayCommand.UnhandledError += exception => Write("app", exception.Message, LogLevel.Error);

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += async (_, _) => await RefreshAsync(quiet: true);

        _currentPage = IsSetupNeeded ? new SetupViewModel(this) : Overview;
        _ = RunLoggedAsync(StartupAsync);
    }

    // ── IServiceHost ─────────────────────────────────────────

    public AppSettings Settings { get; }

    public AmplePaths Paths { get; private set; }

    public StackState Stack { get; private set; }

    public void Write(string source, string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(DateTime.Now, source, message, level);
        if (Dispatcher.UIThread.CheckAccess())
        {
            Append(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Append(entry));
        }
    }

    public void Debug(string source, string message)
    {
        if (Settings.ShowDebug)
        {
            Write(source, message, LogLevel.Debug);
        }
    }

    public void TailLog(string source, string path, int lines)
    {
        IReadOnlyList<string> tail = Shell.Tail(path, lines);
        if (tail.Count == 0)
        {
            Write(source, $"(no lines in {Path.GetFileName(path)})", LogLevel.Warn);
            return;
        }
        foreach (string line in tail)
        {
            Write(source, "  › " + line, LogLevel.Error);
        }
    }

    // ── state ────────────────────────────────────────────────

    /// <summary>Installs and repairs the stack.</summary>
    public StackInstaller Installer { get; }

    /// <summary>The web server.</summary>
    public ServiceViewModel Apache { get; }

    /// <summary>The database.</summary>
    public ServiceViewModel MariaDb { get; }

    /// <summary>Both services, in the order the Overview shows them.</summary>
    public ObservableCollection<ServiceViewModel> Services { get; }

    /// <summary>Every log line, oldest first, trimmed to the settings' maximum.</summary>
    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>The Overview page.</summary>
    public OverviewViewModel Overview { get; }

    /// <summary>The Projects page.</summary>
    public ProjectsViewModel ProjectsPage { get; }

    /// <summary>The Versions page.</summary>
    public VersionsViewModel VersionsPage { get; }

    /// <summary>The database driver, for its active version.</summary>
    public MariaDbService MariaDbService => (MariaDbService)MariaDb.Driver;

    /// <summary>The Logs page.</summary>
    public LogViewModel LogPage { get; }

    /// <summary>The Settings page.</summary>
    public SettingsViewModel SettingsPage { get; }

    /// <summary>The page in the content area.</summary>
    public ObservableObject? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetField(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsOverview));
                OnPropertyChanged(nameof(IsProjects));
                OnPropertyChanged(nameof(IsVersions));
                OnPropertyChanged(nameof(IsLog));
                OnPropertyChanged(nameof(IsSettings));
                OnPropertyChanged(nameof(IsSetup));
            }
        }
    }

    public bool IsOverview => CurrentPage == Overview;
    public bool IsProjects => CurrentPage == ProjectsPage;
    public bool IsVersions => CurrentPage == VersionsPage;
    public bool IsLog => CurrentPage == LogPage;
    public bool IsSettings => CurrentPage == SettingsPage;
    public bool IsSetup => CurrentPage is SetupViewModel;

    /// <summary>True until Apache, MariaDB and one PHP version are installed; the shell then shows Setup.</summary>
    public bool IsSetupNeeded =>
        !Stack.Has("apache") || Paths.InstalledMariaDbVersions().Count == 0 || Paths.InstalledPhpVersions().Count == 0;

    /// <summary>"v0.1.0".</summary>
    public string Version => BuildInfo.Version;

    /// <summary>The PHP version new projects use, or "" before setup.</summary>
    public string DefaultPhp => Stack.DefaultPhp ?? Paths.InstalledPhpVersions().FirstOrDefault() ?? "";

    public RelayCommand ShowOverviewCommand { get; }
    public RelayCommand ShowProjectsCommand { get; }
    public RelayCommand ShowVersionsCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }
    public RelayCommand StartAllCommand { get; }
    public RelayCommand StopAllCommand { get; }
    public RelayCommand OpenLocalhostCommand { get; }

    /// <summary>Shows a page.</summary>
    /// <param name="page">One of the page view models.</param>
    public void Navigate(ObservableObject page) => CurrentPage = page;

    /// <summary>Called by Setup once the stack is installed: reloads everything and shows the Overview.</summary>
    public async Task FinishSetupAsync()
    {
        Stack = StackState.Load(Paths.StackFile);
        OnPropertyChanged(nameof(IsSetupNeeded));
        OnPropertyChanged(nameof(DefaultPhp));
        StartAllCommand.RaiseCanExecuteChanged();
        StopAllCommand.RaiseCanExecuteChanged();
        Navigate(Overview);
        Overview.Refresh();
        ProjectsPage.Reload();
        SettingsPage.Reload();
        if (Settings.StartServicesOnLaunch)
        {
            await StartAllAsync();
        }
    }

    /// <summary>Runs a fire-and-forget task and writes any exception to the log instead of losing it.</summary>
    /// <param name="work">The task to run.</param>
    public async Task RunLoggedAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so the log is the only place the failure can go.
            Write("app", ex.Message, LogLevel.Error);
        }
    }

    /// <summary>Starts polling, repairs paths after a move and auto-starts the services.</summary>
    private async Task StartupAsync()
    {
        Write("app", $"{BuildInfo.AppName} {BuildInfo.Version} · {Paths.Root}");
        if (IsSetupNeeded)
        {
            Write("app", "Setup needed: the stack isn't installed in this folder yet.");
            return;
        }

        try
        {
            Paths.EnsureFolders();
            Installer.RelocateIfMoved();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Write("app", "The AMPLE folder couldn't be prepared: " + ex.Message, LogLevel.Error);
        }

        await RefreshAsync(quiet: false);
        _pollTimer.Start();

        if (Settings.StartServicesOnLaunch)
        {
            await StartAllAsync();
        }
    }

    /// <summary>Starts every installed service that is stopped.</summary>
    public async Task StartAllAsync()
    {
        foreach (ServiceViewModel service in Services)
        {
            await service.StartIfStoppedAsync();
        }
    }

    /// <summary>Stops every running service, the database first so Apache can't hit a closing server.</summary>
    public async Task StopAllAsync()
    {
        foreach (ServiceViewModel service in Services.Reverse())
        {
            await service.StopIfRunningAsync();
        }
    }

    /// <summary>Re-reads the process table and updates every service.</summary>
    /// <param name="quiet">True for the timer's refreshes; kept so callers say which kind they are.</param>
    public async Task RefreshAsync(bool quiet)
    {
        if (_isRefreshing || (quiet && CurrentPage is SetupViewModel))
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var statuses = await Task.Run(() =>
                Services.Select(service => (Service: service, Pids: service.Driver.DetectPids())).ToList());
            foreach ((ServiceViewModel service, IReadOnlyList<int> pids) in statuses)
            {
                service.Apply(pids);
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    /// <summary>Called when a service started or stopped, so the Overview's summary follows.</summary>
    public void NoteStateChanged() => Overview.Refresh();

    /// <summary>Called after versions were installed, switched or removed, so every page re-reads the stack.</summary>
    public void OnStackChanged()
    {
        OnPropertyChanged(nameof(DefaultPhp));
        OnPropertyChanged(nameof(IsSetupNeeded));
        Overview.Refresh();
        SettingsPage.Reload();
        ProjectsPage.Reload();
    }

    /// <summary>Moves a service to another port: settings, configs, and the database tools' connection.</summary>
    /// <param name="driver">The service whose port changes.</param>
    /// <param name="from">The port in use.</param>
    /// <param name="to">The free port to use instead.</param>
    public async Task ChangePortAsync(ServiceDriver driver, int from, int to)
    {
        PortSettings ports = Settings.Ports;
        if (ports.Http == from)
        {
            ports.Http = to;
        }
        else if (ports.Https == from)
        {
            ports.Https = to;
        }
        else if (ports.MariaDb == from)
        {
            ports.MariaDb = to;
        }
        Settings.Save();
        await ApplyPortsAsync();
        Write(driver.Tag, $"Moved from port {from} to port {to}.", LogLevel.Ok);
    }

    /// <summary>Writes the current ports into every config that names them.</summary>
    public async Task ApplyPortsAsync()
    {
        await Task.Run(() =>
        {
            if (DefaultPhp.Length > 0)
            {
                Installer.WriteGeneratedApacheFiles(DefaultPhp);
            }
            MariaDbSetup.SetPort(Paths, Settings.Ports.MariaDb);
        });
        SettingsPage.Reload();
        Overview.Refresh();
    }

    /// <summary>The URL the Open button launches for the chosen database tool.</summary>
    public string DatabaseToolUrl => ServiceDriver.LocalUrl(
        Settings.Ports.Http,
        Settings.DatabaseTool == DatabaseTool.PhpMyAdmin ? "/phpmyadmin/" : "/ledger/");

    /// <summary>Opens the chosen database tool in the browser.</summary>
    public void OpenDatabaseTool() => Shell.OpenUrl(DatabaseToolUrl, Settings.Browser);

    // ── manifest and components ──────────────────────────────

    /// <summary>The bundle's or the local manifest when there is one, else the published one.</summary>
    /// <param name="status">Receives a line while the manifest is being read, and the reason when it fails.</param>
    /// <returns>The manifest; null when it couldn't be read (already logged).</returns>
    public async Task<Manifest?> LoadManifestAsync(Action<string> status)
    {
        try
        {
            if (Manifest.LocalPath(Paths) is { } localPath)
            {
                return Manifest.Load(localPath);
            }
            status("Reading the component list…");
            return await Manifest.FetchAsync(Downloads.Http);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            status("The component list couldn't be read: " + ex.Message);
            Write("install", "The manifest couldn't be read: " + ex.Message, LogLevel.Error);
            return null;
        }
    }

    /// <summary>Installs one component from a manifest and rewrites the configs that depend on it.</summary>
    /// <param name="manifest">The manifest the component comes from.</param>
    /// <param name="component">The component.</param>
    /// <param name="status">Receives the installer's step names and the closing sentence.</param>
    /// <returns>The installer's result.</returns>
    public async Task<InstallResult> InstallComponentAsync(
        Manifest manifest,
        ManifestComponent component,
        Action<string> status)
    {
        var progress = new InstallProgress(status, detail => { }, fraction => { });
        InstallResult result = await Installer.InstallAsync(
            InstallPlan.ForComponent(manifest, component),
            progress,
            CancellationToken.None);
        if (result.Success)
        {
            await Apache.RestartIfRunningAsync();
            status($"{component.Name} {component.Version} installed.");
        }
        else
        {
            status(result.Message);
        }
        Stack = StackState.Load(Paths.StackFile);
        OnPropertyChanged(nameof(DefaultPhp));
        Overview.Refresh();
        SettingsPage.Reload();
        return result;
    }

    /// <summary>Opens phpinfo() for one PHP version in the browser.</summary>
    /// <param name="version">A folder name under bin\php; null or empty opens the default version's.</param>
    public void OpenPhpInfo(string? version = null)
    {
        string wanted = string.IsNullOrEmpty(version) ? DefaultPhp : version;
        if (wanted.Length > 0)
        {
            Shell.OpenUrl(ServiceDriver.LocalUrl(Settings.Ports.Http, $"/_ample/php/{wanted}/"), Settings.Browser);
        }
    }

    // ── log ──────────────────────────────────────────────────

    private void Append(LogEntry entry)
    {
        Log.Add(entry);
        while (Log.Count > Settings.MaxLogLines)
        {
            Log.RemoveAt(0);
        }
        if (Settings.LogToFile)
        {
            AppendToFile(entry);
        }
        if (entry.Level is not LogLevel.Debug)
        {
            Overview.NoteActivity(entry);
        }
    }

    private void AppendToFile(LogEntry entry)
    {
        try
        {
            lock (_logFileLock)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(AppSettings.LogFilePath, entry.AsText + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log file is a convenience; a locked or full disk must not take the app down.
        }
    }

    /// <summary>Stops polling.</summary>
    public void Dispose() => _pollTimer.Stop();
}
