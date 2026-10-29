using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Services;

namespace Ample.App.ViewModels;

/// <summary>The Overview page: one sentence about the stack, the service cards, the database tool and
/// the projects.</summary>
public sealed class OverviewViewModel : ObservableObject
{
    private const int ActivityLines = 6;
    private readonly MainViewModel _main;

    /// <summary>Builds the page.</summary>
    /// <param name="main">The shell.</param>
    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        ToggleAllCommand = new RelayCommand(ToggleAllAsync, () => !_main.IsSetupNeeded);
        PickLedgerCommand = new RelayCommand(() => PickTool(DatabaseTool.Ledger));
        PickPhpMyAdminCommand = new RelayCommand(() => PickTool(DatabaseTool.PhpMyAdmin));
        OpenDatabaseToolCommand = new RelayCommand(_main.OpenDatabaseTool, () => _main.MariaDb.IsRunning);
        OpenLocalhostCommand = _main.OpenLocalhostCommand;
        OpenPhpInfoCommand = new RelayCommand(
            () => _main.OpenPhpInfo(),
            () => _main.Apache.IsRunning && _main.DefaultPhp.Length > 0);
        ShowProjectsCommand = _main.ShowProjectsCommand;
        ShowLogCommand = _main.ShowLogCommand;
        ShowSettingsCommand = _main.ShowSettingsCommand;
    }

    /// <summary>The services, for the cards.</summary>
    public ObservableCollection<ServiceViewModel> Services => _main.Services;

    /// <summary>The last few non-debug log lines, newest first.</summary>
    public ObservableCollection<LogEntry> Activity { get; } = [];

    /// <summary>The projects, newest last, for the summary list.</summary>
    public ObservableCollection<ProjectRowViewModel> Projects { get; } = [];

    public RelayCommand ToggleAllCommand { get; }
    public RelayCommand PickLedgerCommand { get; }
    public RelayCommand PickPhpMyAdminCommand { get; }
    public RelayCommand OpenDatabaseToolCommand { get; }
    public RelayCommand OpenLocalhostCommand { get; }
    public RelayCommand OpenPhpInfoCommand { get; }

    /// <summary>"PHP 8.4.27 info", for the header button.</summary>
    public string PhpInfoLabel => _main.DefaultPhp.Length > 0 ? $"PHP {_main.DefaultPhp} info" : "PHP info";
    public RelayCommand ShowProjectsCommand { get; }
    public RelayCommand ShowLogCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    // ── hero ─────────────────────────────────────────────────

    private int Running => Services.Count(service => service.IsRunning);

    private bool AnyConflict => Services.Any(service => service.HasConflict);

    /// <summary>The headline: "Everything is running", "Your stack is stopped",
    /// "The web server couldn't start".</summary>
    public string HeroTitle
    {
        get
        {
            if (AnyConflict)
            {
                ServiceViewModel blocked = Services.First(service => service.HasConflict);
                return $"The {blocked.Name.ToLowerInvariant()} couldn't start";
            }
            if (Running == Services.Count)
            {
                return "Everything is running";
            }
            return Running == 0 ? "Your stack is stopped" : $"{Running} of {Services.Count} services are running";
        }
    }

    /// <summary>The line under the headline.</summary>
    public string HeroSubtitle
    {
        get
        {
            if (AnyConflict)
            {
                return "Another app is using the port. Switch ports below and you're good to go.";
            }
            if (Running == Services.Count)
            {
                return "Your projects are live and the database is ready.";
            }
            return Running == 0
                ? "Start it to serve your projects locally."
                : "Some of your projects may not work until everything is started.";
        }
    }

    /// <summary>The pill text: "All running", "Stopped", "Needs attention", "1 of 2 running".</summary>
    public string HeroPill
    {
        get
        {
            if (AnyConflict)
            {
                return "Needs attention";
            }
            if (Running == Services.Count)
            {
                return "All running";
            }
            return Running == 0 ? "Stopped" : $"{Running} of {Services.Count} running";
        }
    }

    /// <summary>True when the hero pill shows the running style.</summary>
    public bool HeroRunning => !AnyConflict && Running == Services.Count;

    /// <summary>True when the hero pill shows the attention style.</summary>
    public bool HeroAttention => AnyConflict || (Running > 0 && Running < Services.Count);

    /// <summary>"Stop all" when anything runs, else "Start all".</summary>
    public string ToggleAllLabel => Running > 0 ? "Stop all" : "Start all";

    /// <summary>True when the big button is the primary (blue) Start all.</summary>
    public bool ToggleAllIsPrimary => Running == 0;

    private async Task ToggleAllAsync()
    {
        if (Running > 0)
        {
            await _main.StopAllAsync();
        }
        else
        {
            await _main.StartAllAsync();
        }
    }

    // ── database tool ────────────────────────────────────────

    /// <summary>True when Ledger is the chosen tool.</summary>
    public bool IsLedger => _main.Settings.DatabaseTool == DatabaseTool.Ledger;

    /// <summary>True when phpMyAdmin is the chosen tool.</summary>
    public bool IsPhpMyAdmin => !IsLedger;

    /// <summary>True when phpMyAdmin's folder exists.</summary>
    public bool IsPhpMyAdminInstalled => Directory.Exists(_main.Paths.PhpMyAdmin);

    /// <summary>One line about the chosen tool.</summary>
    public string ToolDescription => IsLedger
        ? "Ledger is fast and modern, with a smart SQL editor and inline editing. Built by CC Software."
        : IsPhpMyAdminInstalled
            ? "phpMyAdmin is the classic choice, familiar if you're coming from XAMPP."
            : "phpMyAdmin isn't installed yet. Install it from Settings › Database tool.";

    /// <summary>"localhost/ledger" or "localhost:8080/phpmyadmin".</summary>
    public string ToolUrl => _main.DatabaseToolUrl.Replace("http://", "", StringComparison.Ordinal).TrimEnd('/');

    /// <summary>"Open Ledger" or "Open phpMyAdmin".</summary>
    public string ToolOpenLabel => IsLedger ? "Open Ledger" : "Open phpMyAdmin";

    /// <summary>True when the database runs, so the tool can open.</summary>
    public bool CanOpenTool => _main.MariaDb.IsRunning && (IsLedger || IsPhpMyAdminInstalled);

    /// <summary>True when the card explains why the tool can't open yet.</summary>
    public bool ShowToolHint => !CanOpenTool;

    /// <summary>Why the tool can't open: the database is stopped, or phpMyAdmin is missing.</summary>
    public string ToolHint => _main.MariaDb.IsRunning
        ? "Install phpMyAdmin in Settings to open it"
        : "Start the database to open it";

    private void PickTool(DatabaseTool tool)
    {
        if (_main.Settings.DatabaseTool == tool)
        {
            return;
        }

        _main.Settings.DatabaseTool = tool;
        _main.Settings.Save();
        _main.Write("app", $"Database tool set to {(tool == DatabaseTool.Ledger ? "Ledger" : "phpMyAdmin")}.");
        Refresh();
        _main.SettingsPage.Reload();
    }

    // ── refresh ──────────────────────────────────────────────

    /// <summary>Re-reads every derived property after a state or settings change.</summary>
    public void Refresh()
    {
        string[] properties =
        [
            nameof(HeroTitle), nameof(HeroSubtitle), nameof(HeroPill), nameof(HeroRunning),
            nameof(HeroAttention), nameof(ToggleAllLabel), nameof(ToggleAllIsPrimary), nameof(IsLedger),
            nameof(IsPhpMyAdmin), nameof(IsPhpMyAdminInstalled), nameof(ToolDescription), nameof(ToolUrl),
            nameof(ToolOpenLabel), nameof(CanOpenTool), nameof(ShowToolHint), nameof(ToolHint),
            nameof(HasProjects), nameof(NoProjects),
        ];
        foreach (string property in properties)
        {
            OnPropertyChanged(property);
        }
        OnPropertyChanged(nameof(PhpInfoLabel));
        ToggleAllCommand.RaiseCanExecuteChanged();
        OpenDatabaseToolCommand.RaiseCanExecuteChanged();
        OpenPhpInfoCommand.RaiseCanExecuteChanged();
        ReloadProjects();
    }

    /// <summary>True when the stack has at least one project.</summary>
    public bool HasProjects => _main.Settings.Projects.Count > 0;

    /// <summary>True when the projects card shows its empty state.</summary>
    public bool NoProjects => !HasProjects;

    private void ReloadProjects()
    {
        Projects.Clear();
        foreach (Project project in _main.Settings.Projects)
        {
            Projects.Add(new ProjectRowViewModel(project, _main));
        }
    }

    /// <summary>Adds a log line to the activity list, dropping the oldest beyond the last few.</summary>
    /// <param name="entry">A non-debug log line.</param>
    public void NoteActivity(LogEntry entry)
    {
        if (entry.Level is LogLevel.Error or LogLevel.Warn or LogLevel.Ok
            || entry.Source is "apache" or "mariadb" or "projects" or "install")
        {
            Activity.Insert(0, entry);
            while (Activity.Count > ActivityLines)
            {
                Activity.RemoveAt(Activity.Count - 1);
            }
        }
    }
}
