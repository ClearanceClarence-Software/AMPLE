using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Sites;
using Ample.Stack;
using Microsoft.Win32;

namespace Ample.App.ViewModels;

/// <summary>The Settings page.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "AMPLE";

    private readonly MainViewModel _main;
    private string _httpPort = "";
    private string _httpsPort = "";
    private string _mariaDbPort = "";
    private string _portsStatus = "";
    private string _toolStatus = "";
    private string _phpStatus = "";
    private string _maintenanceStatus = "";
    private bool _revealPassword;
    private bool _isBusy;
    private string? _selectedDefaultPhp;
    private string? _selectedTheme;

    /// <summary>Builds the page from the settings.</summary>
    /// <param name="main">The shell.</param>
    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        PickLedgerCommand = new RelayCommand(() => PickTool(DatabaseTool.Ledger));
        PickPhpMyAdminCommand = new RelayCommand(() => PickTool(DatabaseTool.PhpMyAdmin));
        InstallPhpMyAdminCommand = new RelayCommand(InstallPhpMyAdminAsync, () => !IsBusy && !IsPhpMyAdminInstalled);
        ApplyPortsCommand = new RelayCommand(ApplyPortsAsync, () => !IsBusy);
        FindFreePortsCommand = new RelayCommand(FindFreePortsAsync, () => !IsBusy);
        OpenPhpIniCommand = new RelayCommand(OpenPhpIni);
        OpenPhpFolderCommand = new RelayCommand(() => Shell.Open(_main.Paths.PhpRoot));
        ToggleRevealCommand = new RelayCommand(() =>
        {
            _revealPassword = !_revealPassword;
            OnPropertyChanged(nameof(PasswordText));
            OnPropertyChanged(nameof(RevealLabel));
        });
        RepairCommand = new RelayCommand(RepairAsync, () => !IsBusy);
        TrustCertificateCommand = new RelayCommand(() => ProjectSetup.TrustAuthorityAsync(_main), () => !IsBusy);
        OpenRootCommand = new RelayCommand(() => Shell.Open(_main.Paths.Root));
        OpenRepositoryCommand = new RelayCommand(() => Shell.Open(BuildInfo.RepositoryUrl));
        OpenLicensesCommand = new RelayCommand(() => Shell.Open(BuildInfo.RepositoryUrl + "/tree/main/LICENSES"));
        Reload();
    }

    // ── database tool ────────────────────────────────────────

    public bool IsLedger => _main.Settings.DatabaseTool == DatabaseTool.Ledger;
    public bool IsPhpMyAdmin => !IsLedger;
    public bool IsPhpMyAdminInstalled => Directory.Exists(_main.Paths.PhpMyAdmin);
    public bool ShowPhpMyAdminInstall => IsPhpMyAdmin && !IsPhpMyAdminInstalled;
    public string PhpMyAdminBadge => IsPhpMyAdminInstalled ? "Installed" : "Optional";

    /// <summary>True signs the tools in as root without a login form.</summary>
    public bool AutoLogin
    {
        get => _main.Settings.DatabaseToolAutoLogin;
        set
        {
            if (_main.Settings.DatabaseToolAutoLogin != value)
            {
                _main.Settings.DatabaseToolAutoLogin = value;
                _main.Settings.Save();
                ApplyAutoLogin(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Writes the auto-login choice into Ledger's and phpMyAdmin's configs.</summary>
    private void ApplyAutoLogin(bool autoLogin)
    {
        string password = _main.Stack.MariaDbRootPassword ?? "";
        bool ledgerChanged = AppsSetup.SetLedgerAutoLogin(_main.Paths, autoLogin);
        bool phpMyAdminChanged = AppsSetup.SetPhpMyAdminAutoLogin(_main.Paths, password, autoLogin);
        if (ledgerChanged || phpMyAdminChanged)
        {
            ToolStatus = autoLogin
                ? "The database tools now open without a login."
                : $"The database tools now ask for a login: user {AppsSetup.LedgerAdminUser} (Ledger) or root, "
                    + "with the password under Database security.";
        }
    }

    public string ToolStatus
    {
        get => _toolStatus;
        private set => SetField(ref _toolStatus, value);
    }

    public RelayCommand PickLedgerCommand { get; }
    public RelayCommand PickPhpMyAdminCommand { get; }
    public RelayCommand InstallPhpMyAdminCommand { get; }

    private void PickTool(DatabaseTool tool)
    {
        _main.Settings.DatabaseTool = tool;
        _main.Settings.Save();
        RaiseTool();
        _main.Overview.Refresh();
    }

    private async Task InstallPhpMyAdminAsync()
    {
        IsBusy = true;
        try
        {
            Manifest? manifest = await _main.LoadManifestAsync(status => ToolStatus = status);
            if (manifest?.Find("phpmyadmin") is { } component)
            {
                await _main.InstallComponentAsync(manifest, component, status => ToolStatus = status);
            }
        }
        finally
        {
            IsBusy = false;
        }
        RaiseTool();
    }

    private void RaiseTool()
    {
        string[] properties =
        [
            nameof(IsLedger), nameof(IsPhpMyAdmin), nameof(IsPhpMyAdminInstalled),
            nameof(ShowPhpMyAdminInstall), nameof(PhpMyAdminBadge),
        ];
        foreach (string property in properties)
        {
            OnPropertyChanged(property);
        }
        InstallPhpMyAdminCommand.RaiseCanExecuteChanged();
    }

    // ── ports ────────────────────────────────────────────────

    public string HttpPort
    {
        get => _httpPort;
        set => SetField(ref _httpPort, value);
    }

    public string HttpsPort
    {
        get => _httpsPort;
        set => SetField(ref _httpsPort, value);
    }

    public string MariaDbPort
    {
        get => _mariaDbPort;
        set => SetField(ref _mariaDbPort, value);
    }

    public string PortsStatus
    {
        get => _portsStatus;
        private set => SetField(ref _portsStatus, value);
    }

    public RelayCommand ApplyPortsCommand { get; }
    public RelayCommand FindFreePortsCommand { get; }

    private async Task ApplyPortsAsync()
    {
        if (!TryParsePort(HttpPort, out int http)
            || !TryParsePort(HttpsPort, out int https)
            || !TryParsePort(MariaDbPort, out int mariaDb))
        {
            PortsStatus = "Ports are numbers from 1 to 65535.";
            return;
        }
        if (http == https || http == mariaDb || https == mariaDb)
        {
            PortsStatus = "Each service needs its own port.";
            return;
        }

        IsBusy = true;
        try
        {
            PortSettings ports = _main.Settings.Ports;
            ports.Http = http;
            ports.Https = https;
            ports.MariaDb = mariaDb;
            _main.Settings.Save();
            await _main.ApplyPortsAsync();
            PortsStatus = "Saved. Running services use the new ports after a restart.";
            _main.Write("app", $"Ports set to {http}, {https} and {mariaDb}.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task FindFreePortsAsync()
    {
        List<Listener> listeners = await Task.Run(PortTable.ListAll);
        var ownPids = new HashSet<int>((await Task.Run(() => _main.Apache.Driver.DetectPids()))
            .Concat(await Task.Run(() => _main.MariaDb.Driver.DetectPids())));
        HttpPort = NextFree(_main.Settings.Ports.Http, 8080, listeners, ownPids).ToString();
        HttpsPort = NextFree(_main.Settings.Ports.Https, 8443, listeners, ownPids).ToString();
        MariaDbPort = NextFree(_main.Settings.Ports.MariaDb, 3307, listeners, ownPids).ToString();
        PortsStatus = "Free ports suggested. Apply to use them.";
    }

    private static int NextFree(int current, int alternative, List<Listener> listeners, HashSet<int> ownPids)
    {
        int? owner = PortTable.OwnerOf(current, listeners);
        if (owner is null || ownPids.Contains(owner.Value))
        {
            return current;
        }

        int candidate = alternative;
        while (PortTable.OwnerOf(candidate, listeners) is not null)
        {
            candidate++;
        }
        return candidate;
    }

    private static bool TryParsePort(string text, out int port) =>
        int.TryParse(text.Trim(), out port) && port is > 0 and < 65536;

    // ── PHP ──────────────────────────────────────────────────

    /// <summary>The installed versions, newest first.</summary>
    public ObservableCollection<string> PhpVersions { get; } = [];

    /// <summary>The version new projects use.</summary>
    public string? SelectedDefaultPhp
    {
        get => _selectedDefaultPhp;
        set
        {
            if (SetField(ref _selectedDefaultPhp, value) && value is not null && value != _main.Stack.DefaultPhp)
            {
                _main.Stack.DefaultPhp = value;
                _main.Stack.Save(_main.Paths.StackFile);
                _ = _main.RunLoggedAsync(() => ApplyDefaultPhpAsync(value));
            }
        }
    }

    public string PhpStatus
    {
        get => _phpStatus;
        private set => SetField(ref _phpStatus, value);
    }

    public RelayCommand OpenPhpIniCommand { get; }
    public RelayCommand OpenPhpFolderCommand { get; }

    private async Task ApplyDefaultPhpAsync(string version)
    {
        await Task.Run(() => _main.Installer.WriteGeneratedApacheFiles(version));
        await _main.Apache.RestartIfRunningAsync();
        PhpStatus = $"PHP {version} is now the default.";
        _main.Write("app", PhpStatus);
        _main.Overview.Refresh();
    }

    private void OpenPhpIni()
    {
        string version = SelectedDefaultPhp ?? _main.DefaultPhp;
        if (version.Length > 0)
        {
            Shell.OpenInEditor(Path.Combine(_main.Paths.Php(version), "php.ini"), _main.Settings.Editor);
        }
    }

    // ── database security ────────────────────────────────────

    public string PasswordText => _revealPassword
        ? _main.Stack.MariaDbRootPassword ?? "(none set)"
        : new string('•', Math.Max(8, _main.Stack.MariaDbRootPassword?.Length ?? 0));

    public string RevealLabel => _revealPassword ? "Hide" : "Show";

    /// <summary>The root password in clear, for the copy button.</summary>
    public string? RootPassword => _main.Stack.MariaDbRootPassword;

    public RelayCommand ToggleRevealCommand { get; }

    // ── startup and appearance ───────────────────────────────

    public bool LaunchAtStartup
    {
        get => _main.Settings.LaunchAtStartup;
        set
        {
            if (_main.Settings.LaunchAtStartup != value)
            {
                _main.Settings.LaunchAtStartup = value;
                _main.Settings.Save();
                SetRunAtStartup(value);
                OnPropertyChanged();
            }
        }
    }

    public bool StartServicesOnLaunch
    {
        get => _main.Settings.StartServicesOnLaunch;
        set
        {
            if (_main.Settings.StartServicesOnLaunch != value)
            {
                _main.Settings.StartServicesOnLaunch = value;
                _main.Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool KeepInTray
    {
        get => _main.Settings.KeepInTray;
        set
        {
            if (_main.Settings.KeepInTray != value)
            {
                _main.Settings.KeepInTray = value;
                _main.Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool StartMinimized
    {
        get => _main.Settings.StartMinimized;
        set
        {
            if (_main.Settings.StartMinimized != value)
            {
                _main.Settings.StartMinimized = value;
                _main.Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The theme names for the combo box.</summary>
    public IReadOnlyList<string> Themes { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];

    public string? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetField(ref _selectedTheme, value) && value is not null)
            {
                _main.Settings.Theme = value;
                _main.Settings.Save();
                ThemeSwitcher.Apply(value);
            }
        }
    }

    /// <summary>The editor for config files; empty uses Notepad.</summary>
    public string Editor
    {
        get => _main.Settings.Editor ?? "";
        set
        {
            string? editor = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_main.Settings.Editor != editor)
            {
                _main.Settings.Editor = editor;
                _main.Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The browser for Open links; empty uses the system default.</summary>
    public string Browser
    {
        get => _main.Settings.Browser ?? "";
        set
        {
            string? browser = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_main.Settings.Browser != browser)
            {
                _main.Settings.Browser = browser;
                _main.Settings.Save();
                OnPropertyChanged();
            }
        }
    }

    private static void SetRunAtStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    // ── maintenance and about ────────────────────────────────

    public string MaintenanceStatus
    {
        get => _maintenanceStatus;
        private set => SetField(ref _maintenanceStatus, value);
    }

    public string RootFolder => _main.Paths.Root;
    public string VersionText => $"{BuildInfo.AppName} {BuildInfo.Version} · built {BuildInfo.CompiledOn}";
    public string ComponentsText => string.Join("  ·  ", _main.Stack.Components
        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
        .Select(entry => $"{entry.Key} {entry.Value.Version}"));

    public RelayCommand RepairCommand { get; }
    public RelayCommand TrustCertificateCommand { get; }
    public RelayCommand OpenRootCommand { get; }
    public RelayCommand OpenRepositoryCommand { get; }
    public RelayCommand OpenLicensesCommand { get; }

    /// <summary>Rewrites every missing config and the generated files, and installs what the manifest says is
    /// missing.</summary>
    private async Task RepairAsync()
    {
        IsBusy = true;
        try
        {
            MaintenanceStatus = "Checking the stack…";
            Manifest? manifest = await _main.LoadManifestAsync(status => MaintenanceStatus = status);
            InstallResult result;
            if (manifest is not null)
            {
                InstallPlan plan = InstallPlan.Missing(manifest, _main.Stack, _main.Paths);
                result = await _main.Installer.InstallAsync(
                    plan,
                    Progress(status => MaintenanceStatus = status),
                    CancellationToken.None);
            }
            else
            {
                result = await _main.Installer.ConfigureAsync(
                    Progress(status => MaintenanceStatus = status),
                    CancellationToken.None);
            }
            MaintenanceStatus = result.Message;
            Reload();
            _main.Overview.Refresh();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static InstallProgress Progress(Action<string> status) =>
        new(status, detail => { }, fraction => { });

    private bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            RelayCommand[] commands =
            [
                InstallPhpMyAdminCommand, ApplyPortsCommand, FindFreePortsCommand, RepairCommand,
                TrustCertificateCommand,
            ];
            foreach (RelayCommand command in commands)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Re-reads every value from the settings and the stack.</summary>
    public void Reload()
    {
        PortSettings ports = _main.Settings.Ports;
        HttpPort = ports.Http.ToString();
        HttpsPort = ports.Https.ToString();
        MariaDbPort = ports.MariaDb.ToString();

        PhpVersions.Clear();
        foreach (string version in _main.Paths.InstalledPhpVersions())
        {
            PhpVersions.Add(version);
        }
        _selectedDefaultPhp = _main.Stack.DefaultPhp ?? PhpVersions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedDefaultPhp));
        _selectedTheme = AppTheme.Normalize(_main.Settings.Theme);
        OnPropertyChanged(nameof(SelectedTheme));

        RaiseTool();
        string[] properties =
        [
            nameof(AutoLogin), nameof(PasswordText), nameof(RootPassword), nameof(LaunchAtStartup),
            nameof(StartServicesOnLaunch), nameof(KeepInTray), nameof(StartMinimized), nameof(Editor),
            nameof(Browser), nameof(RootFolder), nameof(ComponentsText),
        ];
        foreach (string property in properties)
        {
            OnPropertyChanged(property);
        }
    }
}
