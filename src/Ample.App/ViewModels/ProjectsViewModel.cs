using System.Collections.ObjectModel;
using Ample.Core;
using Ample.Sites;

namespace Ample.App.ViewModels;

/// <summary>The Projects page: edit the list, then Apply writes the virtual hosts, hosts file and
/// certificate.</summary>
public sealed class ProjectsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string _status = "";
    private bool _isApplying;

    /// <summary>Builds the page from the settings.</summary>
    /// <param name="main">The shell.</param>
    public ProjectsViewModel(MainViewModel main)
    {
        _main = main;
        AddCommand = new RelayCommand(Add);
        ApplyCommand = new RelayCommand(ApplyAsync, () => !IsApplying);
        RevertCommand = new RelayCommand(Reload, () => !IsApplying);
        TrustCertificateCommand = new RelayCommand(() => ProjectSetup.TrustAuthorityAsync(_main), () => !IsApplying);
        Reload();
    }

    /// <summary>The rows being edited.</summary>
    public ObservableCollection<ProjectRowViewModel> Rows { get; } = [];

    /// <summary>One line under the buttons: a validation problem, progress or the last result.</summary>
    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>True while Apply runs.</summary>
    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (SetField(ref _isApplying, value))
            {
                ApplyCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
                TrustCertificateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>True when the list is empty, for the empty state.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The folder new projects are suggested in.</summary>
    public string ProjectsFolder => _main.Paths.Projects;

    public RelayCommand AddCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand RevertCommand { get; }
    public RelayCommand TrustCertificateCommand { get; }

    /// <summary>Adds an empty row with a suggested folder under projects\.</summary>
    public void Add()
    {
        string name = $"Project {Rows.Count + 1}";
        AddFolder(
            Path.Combine(
                _main.Paths.Projects,
                ProjectSetup.SuggestHost(name).Replace(".test", "", StringComparison.Ordinal)),
            name);
    }

    /// <summary>Adds a row for a folder the user picked, naming it after the folder.</summary>
    /// <param name="folder">The document root.</param>
    /// <param name="name">The project's name; null uses the folder name.</param>
    public void AddFolder(string folder, string? name = null)
    {
        string projectName = name ?? Path.GetFileName(folder.TrimEnd('\\', '/'));
        var row = new ProjectRowViewModel(new Project
        {
            Name = projectName,
            Host = ProjectSetup.SuggestHost(projectName),
            Folder = folder,
            UseHttps = true,
        }, _main);
        Rows.Add(row);
        OnPropertyChanged(nameof(IsEmpty));
        Status = "";
    }

    /// <summary>Removes a row; nothing changes on disk until Apply.</summary>
    /// <param name="row">The row.</param>
    public void Remove(ProjectRowViewModel row)
    {
        Rows.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Throws away the edits and shows the projects as saved.</summary>
    public void Reload()
    {
        Rows.Clear();
        foreach (Project project in _main.Settings.Projects)
        {
            Rows.Add(new ProjectRowViewModel(project, _main));
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ProjectsFolder));
    }

    private async Task ApplyAsync()
    {
        List<Project> projects = Rows.Select(row => row.ToProject()).ToList();
        if (ProjectSetup.FindProblem(projects) is { } problem)
        {
            Status = problem;
            return;
        }

        IsApplying = true;
        try
        {
            ProjectSetupResult result = await ProjectSetup.ApplyAsync(
                _main,
                projects,
                _main.Apache.RestartIfRunningAsync,
                line => Status = line);
            Status = result.Message;
            if (result.IsSuccess)
            {
                _main.Installer.WriteLocalhostPages();
                Reload();
                _main.Overview.Refresh();
            }
        }
        finally
        {
            IsApplying = false;
        }
    }
}
