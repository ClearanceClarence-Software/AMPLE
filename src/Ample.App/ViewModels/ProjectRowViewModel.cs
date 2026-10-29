using Ample.Core;
using Ample.Services;

namespace Ample.App.ViewModels;

/// <summary>One project, as a row on the Overview and an editable row on the Projects page.</summary>
public sealed class ProjectRowViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _name;
    private string _host;
    private string _folder;
    private string _phpVersion;
    private string _phpIni;
    private bool _useHttps;

    /// <summary>Wraps a project from the settings.</summary>
    /// <param name="project">The project; edits stay in this row until the page applies them.</param>
    /// <param name="main">The shell, for the ports, the PHP versions and the browser.</param>
    public ProjectRowViewModel(Project project, MainViewModel main)
    {
        _main = main;
        _name = project.Name;
        _host = project.Host;
        _folder = project.Folder;
        _phpVersion = project.PhpVersion ?? DefaultChoice;
        _phpIni = project.PhpIni ?? "";
        _useHttps = project.UseHttps;
        CreateIniCommand = new RelayCommand(CreateIni, () => !HasCustomIni);
        EditIniCommand = new RelayCommand(() => Shell.OpenInEditor(PhpIni, _main.Settings.Editor), () => HasCustomIni);
        ClearIniCommand = new RelayCommand(() => PhpIni = "", () => HasCustomIni);
        OpenCommand = new RelayCommand(() => Shell.OpenUrl(Url, _main.Settings.Browser));
        OpenFolderCommand = new RelayCommand(() => Shell.Open(Folder), () => Directory.Exists(Folder));
    }

    /// <summary>The choice in the PHP list that means "the stack's default version".</summary>
    public const string DefaultChoice = "Default";

    /// <summary>The project's display name.</summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    /// <summary>The host name, e.g. flix.test.</summary>
    public string Host
    {
        get => _host;
        set
        {
            if (SetField(ref _host, value))
            {
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    /// <summary>The document root.</summary>
    public string Folder
    {
        get => _folder;
        set
        {
            if (SetField(ref _folder, value))
            {
                OpenFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The PHP version folder, or <see cref="DefaultChoice"/>.</summary>
    public string PhpVersion
    {
        get => _phpVersion;
        set
        {
            if (SetField(ref _phpVersion, value))
            {
                OnPropertyChanged(nameof(PhpLabel));
            }
        }
    }

    /// <summary>Full path of the project's own php.ini; empty uses the version's php.ini.</summary>
    public string PhpIni
    {
        get => _phpIni;
        set
        {
            if (SetField(ref _phpIni, value))
            {
                OnPropertyChanged(nameof(HasCustomIni));
                OnPropertyChanged(nameof(IniLabel));
                CreateIniCommand.RaiseCanExecuteChanged();
                EditIniCommand.RaiseCanExecuteChanged();
                ClearIniCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when the project has its own php.ini.</summary>
    public bool HasCustomIni => PhpIni.Length > 0;

    /// <summary>"Version's php.ini" or the custom file's name.</summary>
    public string IniLabel => HasCustomIni ? Path.GetFileName(PhpIni) : "version's php.ini";

    /// <summary>Copies the chosen version's php.ini to etc\php\&lt;host&gt;.ini and uses that from now on.</summary>
    public RelayCommand CreateIniCommand { get; }

    /// <summary>Opens the custom php.ini in the editor.</summary>
    public RelayCommand EditIniCommand { get; }

    /// <summary>Goes back to the version's php.ini; the custom file stays on disk.</summary>
    public RelayCommand ClearIniCommand { get; }

    private void CreateIni()
    {
        string version = PhpVersion == DefaultChoice ? _main.DefaultPhp : PhpVersion;
        string source = Path.Combine(_main.Paths.Php(version), "php.ini");
        string hostName = Host.Trim().ToLowerInvariant();
        string target = Path.Combine(_main.Paths.EtcPhp, (hostName.Length > 0 ? hostName : "project") + ".ini");
        Directory.CreateDirectory(_main.Paths.EtcPhp);
        if (!File.Exists(target))
        {
            if (!File.Exists(source))
            {
                _main.Write("projects", $"PHP {version} has no php.ini to copy yet.", LogLevel.Warn);
                return;
            }
            File.Copy(source, target);
        }
        PhpIni = target;
        Shell.OpenInEditor(target, _main.Settings.Editor);
    }

    /// <summary>True serves the project over HTTPS too.</summary>
    public bool UseHttps
    {
        get => _useHttps;
        set
        {
            if (SetField(ref _useHttps, value))
            {
                OnPropertyChanged(nameof(Url));
            }
        }
    }

    /// <summary>The PHP choices: Default first, then every installed version.</summary>
    public IReadOnlyList<string> PhpChoices => [DefaultChoice, .. _main.Paths.InstalledPhpVersions()];

    /// <summary>"PHP 8.4.27" for the Overview, naming the default when the project has none of its own.</summary>
    public string PhpLabel => "PHP " + (PhpVersion == DefaultChoice ? _main.DefaultPhp : PhpVersion);

    /// <summary>The address to open: HTTPS when on, with the port when it isn't the default.</summary>
    public string Url
    {
        get
        {
            PortSettings ports = _main.Settings.Ports;
            if (UseHttps)
            {
                return ports.Https == 443 ? $"https://{Host}/" : $"https://{Host}:{ports.Https}/";
            }
            return ServiceDriver.LocalUrl(ports.Http).Replace("localhost", Host, StringComparison.Ordinal);
        }
    }

    /// <summary>Opens the project in the browser.</summary>
    public RelayCommand OpenCommand { get; }

    /// <summary>Opens the project's folder in Explorer.</summary>
    public RelayCommand OpenFolderCommand { get; }

    /// <summary>The row as a settings entry, trimmed and lowercased where it matters.</summary>
    /// <returns>A new project.</returns>
    public Project ToProject() => new()
    {
        Name = Name.Trim(),
        Host = Host.Trim().ToLowerInvariant(),
        Folder = Folder.Trim(),
        PhpVersion = PhpVersion == DefaultChoice ? null : PhpVersion,
        PhpIni = HasCustomIni ? PhpIni : null,
        UseHttps = UseHttps,
    };
}
