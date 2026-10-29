using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Services;
using Ample.Stack;
using Avalonia.Threading;

namespace Ample.App.ViewModels;

/// <summary>One PHP version on the setup screen.</summary>
public sealed class PhpChoiceViewModel(ManifestComponent component) : ObservableObject
{
    private bool _isSelected = component.Default;

    /// <summary>The manifest entry.</summary>
    public ManifestComponent Component { get; } = component;

    /// <summary>"PHP 8.4.27" plus "(default)" on the default version.</summary>
    public string Label => $"PHP {Component.Version}" + (Component.Default ? "  ·  default" : "");

    /// <summary>True installs this version; the default can't be turned off.</summary>
    public bool IsSelected
    {
        get => _isSelected || Component.Default;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>False on the default version, so its checkbox is locked on.</summary>
    public bool CanToggle => !Component.Default;
}

/// <summary>The first-run screen: where the stack goes, what to install, and the progress while it installs.</summary>
public sealed class SetupViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cancellation;
    private Manifest? _manifest;
    private string _manifestStatus = "Reading the component list…";
    private string _step = "";
    private string _detail = "";
    private double? _fraction;
    private bool _isInstalling;
    private bool _isDone;
    private bool _includePhpMyAdmin;
    private string _resultMessage = "";

    /// <summary>Builds the screen and starts reading the manifest.</summary>
    /// <param name="main">The shell.</param>
    public SetupViewModel(MainViewModel main)
    {
        _main = main;
        InstallCommand = new RelayCommand(InstallAsync, () => _manifest is not null && !IsInstalling && !IsDone);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsInstalling);
        RetryManifestCommand = new RelayCommand(LoadManifestAsync, () => !IsInstalling);
        OpenFolderCommand = new RelayCommand(
            () => Shell.Open(_main.Paths.Root),
            () => Directory.Exists(_main.Paths.Root));
        FinishCommand = new RelayCommand(_main.FinishSetupAsync, () => IsDone);
        _ = LoadManifestAsync();
    }

    /// <summary>Where the stack will be installed: the folder AMPLE.exe runs from.</summary>
    public string Root => _main.Paths.Root;

    /// <summary>True when the exe sits in a folder that looks like a download location rather than a home.</summary>
    public bool RootLooksTemporary
    {
        get
        {
            string root = Root.ToLowerInvariant();
            return root.Contains("\\downloads", StringComparison.Ordinal)
                || root.Contains("\\temp", StringComparison.Ordinal)
                || root.Contains("\\desktop", StringComparison.Ordinal);
        }
    }

    /// <summary>The PHP versions the manifest offers.</summary>
    public ObservableCollection<PhpChoiceViewModel> PhpChoices { get; } = [];

    /// <summary>True also installs phpMyAdmin next to Ledger.</summary>
    public bool IncludePhpMyAdmin
    {
        get => _includePhpMyAdmin;
        set => SetField(ref _includePhpMyAdmin, value);
    }

    /// <summary>"Reading the component list…", "Ready to install", or why the manifest couldn't be read.</summary>
    public string ManifestStatus
    {
        get => _manifestStatus;
        private set => SetField(ref _manifestStatus, value);
    }

    /// <summary>True once the manifest is read, so Install can go ahead.</summary>
    public bool IsReady => _manifest is not null;

    /// <summary>"MariaDB 11.8.9" for the options; other versions are added on the Versions page.</summary>
    public string MariaDbText => _manifest is null
        ? ""
        : $"MariaDB {Manifest.DefaultOf(_manifest.MariaDbVersions)?.Version} "
            + "(more versions on the Versions page afterwards)";

    /// <summary>True when the manifest came from the bundle folder, so nothing is downloaded.</summary>
    public bool IsOffline => File.Exists(Path.Combine(_main.Paths.Bundle, "manifest.json"));

    /// <summary>True when a manifest.json next to the exe is used instead of the published one.</summary>
    public bool IsLocalManifest => !IsOffline && Manifest.LocalPath(_main.Paths) is not null;

    /// <summary>"Nothing will be downloaded" or the download summary.</summary>
    public string SourceText => IsOffline
        ? "Everything is in the bundle folder; nothing will be downloaded."
        : IsLocalManifest
            ? "Using the manifest.json next to AMPLE.exe. Components are downloaded and verified against its checksums."
            : "Components are downloaded from their official sites and verified against the manifest's checksums.";

    public string Step
    {
        get => _step;
        private set => SetField(ref _step, value);
    }

    public string Detail
    {
        get => _detail;
        private set => SetField(ref _detail, value);
    }

    /// <summary>0..1 for the bar, or null for busy.</summary>
    public double? Fraction
    {
        get => _fraction;
        private set
        {
            if (SetField(ref _fraction, value))
            {
                OnPropertyChanged(nameof(IsIndeterminate));
                OnPropertyChanged(nameof(Percent));
            }
        }
    }

    public bool IsIndeterminate => Fraction is null;
    public double Percent => (Fraction ?? 0) * 100;

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (SetField(ref _isInstalling, value))
            {
                OnPropertyChanged(nameof(ShowOptions));
                RaiseCommands();
            }
        }
    }

    public bool IsDone
    {
        get => _isDone;
        private set
        {
            if (SetField(ref _isDone, value))
            {
                OnPropertyChanged(nameof(ShowOptions));
                RaiseCommands();
            }
        }
    }

    /// <summary>True shows the choices; false shows the progress or the result.</summary>
    public bool ShowOptions => !IsInstalling && !IsDone;

    /// <summary>The installer's closing sentence.</summary>
    public string ResultMessage
    {
        get => _resultMessage;
        private set => SetField(ref _resultMessage, value);
    }

    /// <summary>The last log lines, for the progress card.</summary>
    public ObservableCollection<LogEntry> RecentLog => _main.Log;

    public RelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryManifestCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand FinishCommand { get; }

    private async Task LoadManifestAsync()
    {
        ManifestStatus = "Reading the component list…";
        _manifest = null;
        RaiseCommands();
        try
        {
            Manifest manifest = Manifest.LocalPath(_main.Paths) is { } localPath
                ? Manifest.Load(localPath)
                : await Manifest.FetchAsync(Downloads.Http);
            List<string> problems = manifest.Validate();
            if (problems.Count > 0)
            {
                ManifestStatus = "The component list is damaged: " + problems[0];
                return;
            }

            PhpChoices.Clear();
            foreach (ManifestComponent php in manifest.PhpVersions)
            {
                PhpChoices.Add(new PhpChoiceViewModel(php));
            }
            _manifest = manifest;
            ManifestStatus = $"Ready · component list from {manifest.Updated}";
            OnPropertyChanged(nameof(MariaDbText));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Text.Json.JsonException
            or OperationCanceledException)
        {
            ManifestStatus = "The component list couldn't be read. Check your connection and try again. " + ex.Message;
            _main.Write("install", "The manifest couldn't be read: " + ex.Message, LogLevel.Error);
        }
        finally
        {
            RaiseCommands();
        }
    }

    private async Task InstallAsync()
    {
        if (_manifest is null)
        {
            return;
        }

        var phpVersions = PhpChoices
            .Where(choice => choice.IsSelected)
            .Select(choice => choice.Component.Version)
            .ToList();
        InstallPlan plan = InstallPlan.Fresh(_manifest, phpVersions, IncludePhpMyAdmin);
        _cancellation = new CancellationTokenSource();
        IsInstalling = true;
        Fraction = null;
        var progress = new InstallProgress(
            step => Dispatcher.UIThread.Post(() => Step = step),
            detail => Dispatcher.UIThread.Post(() => Detail = detail),
            fraction => Dispatcher.UIThread.Post(() => Fraction = fraction));
        try
        {
            InstallResult result = await _main.Installer.InstallAsync(plan, progress, _cancellation.Token);
            ResultMessage = result.Message;
            if (result.Success)
            {
                Step = "Done";
                Detail = "";
                Fraction = 1;
                IsDone = true;
            }
            else
            {
                // The options come back with the reason in their status line, so Install can be tried again.
                ManifestStatus = result.Message;
            }
        }
        finally
        {
            IsInstalling = false;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>Releases the cancellation source of an install that is still running.</summary>
    public void Dispose()
    {
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void RaiseCommands()
    {
        OnPropertyChanged(nameof(IsReady));
        InstallCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RetryManifestCommand.RaiseCanExecuteChanged();
        FinishCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
    }
}
