using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Services;
using Ample.Stack;

namespace Ample.App.ViewModels;

/// <summary>One PHP or MariaDB version on the Versions page: installed or available, current or not.</summary>
public sealed class VersionRowViewModel : ObservableObject
{
    private readonly VersionsViewModel _page;

    /// <summary>Builds a row.</summary>
    /// <param name="page">The page, which carries out the actions.</param>
    /// <param name="kind">"php" or "mariadb".</param>
    /// <param name="version">The full version.</param>
    /// <param name="component">The manifest entry; null when the version is installed but no longer listed.</param>
    /// <param name="isInstalled">True when its folder holds the marker file.</param>
    /// <param name="isCurrent">True for the default PHP or the active MariaDB.</param>
    public VersionRowViewModel(
        VersionsViewModel page,
        string kind,
        string version,
        ManifestComponent? component,
        bool isInstalled,
        bool isCurrent)
    {
        _page = page;
        Kind = kind;
        Version = version;
        Component = component;
        IsInstalled = isInstalled;
        IsCurrent = isCurrent;
        InstallCommand = new RelayCommand(
            () => _page.InstallAsync(this),
            () => !IsInstalled && Component is not null && !_page.IsBusy);
        MakeCurrentCommand = new RelayCommand(
            () => _page.MakeCurrentAsync(this),
            () => IsInstalled && !IsCurrent && !_page.IsBusy);
        RemoveCommand = new RelayCommand(
            () => _page.RemoveAsync(this),
            () => IsInstalled && !IsCurrent && !_page.IsBusy);
        PhpInfoCommand = new RelayCommand(() => _page.OpenPhpInfo(this), () => IsInstalled && IsPhp);
        EditIniCommand = new RelayCommand(() => _page.EditIni(this), () => IsInstalled && IsPhp);
    }

    public string Kind { get; }
    public string Version { get; }
    public ManifestComponent? Component { get; }
    public bool IsInstalled { get; }
    public bool IsCurrent { get; }
    public bool IsPhp => Kind == "php";

    /// <summary>"PHP 8.4.27" or "MariaDB 11.8.9".</summary>
    public string Title => (IsPhp ? "PHP " : "MariaDB ") + Version;

    /// <summary>"Default", "Active", "Installed" or "Available".</summary>
    public string Badge => IsCurrent ? (IsPhp ? "Default" : "Active") : IsInstalled ? "Installed" : "Available";

    /// <summary>True when the badge shows the running style.</summary>
    public bool BadgeIsCurrent => IsCurrent;

    /// <summary>One line under the title.</summary>
    public string Detail
    {
        get
        {
            if (!IsInstalled)
            {
                return Component is null
                    ? ""
                    : $"{FileTools.FormatSize(Component.Size)} download from {Component.Homepage}";
            }
            if (IsPhp)
            {
                return IsCurrent
                    ? "New projects and localhost run on this version."
                    : "Pick it per project on the Projects page.";
            }
            return IsCurrent ? "Serving on the database port." : "Installed; its data folder is kept separately.";
        }
    }

    /// <summary>"Set as default" or "Make active".</summary>
    public string MakeCurrentLabel => IsPhp ? "Set as default" : "Make active";

    public RelayCommand InstallCommand { get; }
    public RelayCommand MakeCurrentCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand PhpInfoCommand { get; }
    public RelayCommand EditIniCommand { get; }

    /// <summary>Re-evaluates the buttons after the page's busy state changed.</summary>
    public void RaiseCommands()
    {
        InstallCommand.RaiseCanExecuteChanged();
        MakeCurrentCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>The Versions page: every PHP and MariaDB version, installed side by side, installed or removed
/// here.</summary>
public sealed class VersionsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private Manifest? _manifest;
    private string _status = "";
    private bool _isBusy;

    /// <summary>Builds the page; the manifest is read the first time it is shown.</summary>
    /// <param name="main">The shell.</param>
    public VersionsViewModel(MainViewModel main)
    {
        _main = main;
        RefreshCommand = new RelayCommand(LoadAsync, () => !IsBusy);
    }

    /// <summary>The PHP rows, newest first.</summary>
    public ObservableCollection<VersionRowViewModel> Php { get; } = [];

    /// <summary>The MariaDB rows, newest first.</summary>
    public ObservableCollection<VersionRowViewModel> MariaDb { get; } = [];

    /// <summary>One line about the last action or the manifest.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>True while an install, switch or removal runs.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                foreach (VersionRowViewModel row in Php.Concat(MariaDb))
                {
                    row.RaiseCommands();
                }
            }
        }
    }

    /// <summary>"Component list from 2026-10-29" once the manifest is read.</summary>
    public string ManifestText => _manifest is null ? "" : $"Component list from {_manifest.Updated}";

    public RelayCommand RefreshCommand { get; }

    /// <summary>Reads the manifest (once) and rebuilds the rows from it and the installed folders.</summary>
    public async Task LoadAsync()
    {
        _manifest ??= await _main.LoadManifestAsync(status => Status = status);
        OnPropertyChanged(nameof(ManifestText));
        Rebuild();
    }

    /// <summary>Rebuilds the rows from the manifest and what is on disk.</summary>
    public void Rebuild()
    {
        AmplePaths paths = _main.Paths;
        Fill(Php, "php", _manifest?.PhpVersions ?? [], paths.InstalledPhpVersions(), _main.DefaultPhp);
        Fill(
            MariaDb,
            "mariadb",
            _manifest?.MariaDbVersions ?? [],
            paths.InstalledMariaDbVersions(),
            _main.MariaDbService.ActiveVersion);
    }

    private void Fill(
        ObservableCollection<VersionRowViewModel> rows,
        string kind,
        List<ManifestComponent> listed,
        List<string> installed,
        string current)
    {
        rows.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<(string Version, ManifestComponent? Component)>();
        foreach (ManifestComponent component in listed)
        {
            if (seen.Add(component.Version))
            {
                all.Add((component.Version, component));
            }
        }
        foreach (string version in installed)
        {
            if (seen.Add(version))
            {
                all.Add((version, null));
            }
        }

        IEnumerable<(string Version, ManifestComponent? Component)> ordered =
            all.OrderByDescending(entry => VersionUtil.Parse(entry.Version));
        foreach ((string version, ManifestComponent? component) in ordered)
        {
            bool isInstalled = installed.Contains(version, StringComparer.OrdinalIgnoreCase);
            bool isCurrent = isInstalled && version.Equals(current, StringComparison.OrdinalIgnoreCase);
            rows.Add(new VersionRowViewModel(this, kind, version, component, isInstalled, isCurrent));
        }
    }

    /// <summary>Downloads and installs a listed version.</summary>
    /// <param name="row">An available row.</param>
    public async Task InstallAsync(VersionRowViewModel row)
    {
        if (_manifest is null || row.Component is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _main.InstallComponentAsync(
                _manifest,
                row.Component,
                status => Status = status);
        }
        finally
        {
            IsBusy = false;
            Rebuild();
        }
    }

    /// <summary>Makes a PHP version the default, or a MariaDB version the one that runs.</summary>
    /// <param name="row">An installed row that isn't current yet.</param>
    public async Task MakeCurrentAsync(VersionRowViewModel row)
    {
        IsBusy = true;
        try
        {
            if (row.IsPhp)
            {
                _main.Stack.DefaultPhp = row.Version;
                _main.Stack.Save(_main.Paths.StackFile);
                await Task.Run(() => _main.Installer.WriteGeneratedApacheFiles(row.Version));
                _main.Installer.WriteLocalhostPages();
                await _main.Apache.RestartIfRunningAsync();
                Status = $"PHP {row.Version} is now the default.";
            }
            else
            {
                bool wasRunning = _main.MariaDb.IsRunning;
                Status = $"Switching to MariaDB {row.Version}…";
                await _main.MariaDb.StopIfRunningAsync();
                _main.Stack.ActiveMariaDb = row.Version;
                _main.Stack.Save(_main.Paths.StackFile);
                InstallResult configured = await _main.Installer.ConfigureAsync(
                    new InstallProgress(step => Status = step, detail => { }, fraction => { }),
                    CancellationToken.None);
                if (!configured.Success)
                {
                    Status = configured.Message;
                    return;
                }
                if (wasRunning)
                {
                    await _main.MariaDb.StartIfStoppedAsync();
                }
                Status = $"MariaDB {row.Version} is now active" + (wasRunning ? " and running." : ".");
            }
            _main.Write("app", Status, LogLevel.Ok);
        }
        finally
        {
            IsBusy = false;
            _main.OnStackChanged();
            Rebuild();
        }
    }

    /// <summary>Deletes an installed version's binaries. A MariaDB version's data folder is kept.</summary>
    /// <param name="row">An installed row that isn't current.</param>
    public async Task RemoveAsync(VersionRowViewModel row)
    {
        IsBusy = true;
        try
        {
            string folder = row.IsPhp ? _main.Paths.Php(row.Version) : _main.Paths.MariaDb(row.Version);
            await Task.Run(() =>
            {
                FileTools.ClearReadOnly(folder);
                Directory.Delete(folder, recursive: true);
            });
            string stackId = row.IsPhp ? StackState.PhpId(row.Version) : StackState.MariaDbId(row.Version);
            _main.Stack.Components.Remove(stackId);
            _main.Stack.Save(_main.Paths.StackFile);
            if (row.IsPhp)
            {
                await Task.Run(() => _main.Installer.WriteGeneratedApacheFiles(_main.DefaultPhp));
                _main.Installer.WriteLocalhostPages();
                await _main.Apache.RestartIfRunningAsync();
            }
            Status = row.IsPhp
                ? $"PHP {row.Version} removed."
                : $"MariaDB {row.Version} removed; its data folder was kept under data\\mariadb\\{row.Version}.";
            _main.Write("app", Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't remove {row.Title}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _main.OnStackChanged();
            Rebuild();
        }
    }

    /// <summary>Opens a version's phpinfo page.</summary>
    /// <param name="row">A PHP row.</param>
    public void OpenPhpInfo(VersionRowViewModel row) => _main.OpenPhpInfo(row.Version);

    /// <summary>Opens a version's php.ini in the editor.</summary>
    /// <param name="row">A PHP row.</param>
    public void EditIni(VersionRowViewModel row) =>
        Shell.OpenInEditor(Path.Combine(_main.Paths.Php(row.Version), "php.ini"), _main.Settings.Editor);
}
