using Ample.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Ample.App.Views;

/// <summary>Code-behind for Projects: the folder pickers and the remove buttons.</summary>
public partial class ProjectsView : UserControl
{
    public ProjectsView() => InitializeComponent();

    private ProjectsViewModel? ViewModel => DataContext as ProjectsViewModel;

    /// <summary>Adds a project for a folder the user picks.</summary>
    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        string? folder = await PickFolderAsync("Choose the project's folder", ViewModel.ProjectsFolder);
        if (folder is not null)
        {
            ViewModel.AddFolder(folder);
        }
    }

    /// <summary>Changes the folder of the row whose Browse button was clicked.</summary>
    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ProjectRowViewModel row)
        {
            return;
        }

        string? folder = await PickFolderAsync($"Choose the folder for {row.Name}", row.Folder);
        if (folder is not null)
        {
            row.Folder = folder;
        }
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is ProjectRowViewModel row)
        {
            ViewModel?.Remove(row);
        }
    }

    /// <summary>Opens the system folder picker.</summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="startIn">A folder to open in; ignored when it doesn't exist.</param>
    /// <returns>The chosen folder's full path; null when the dialog was canceled.</returns>
    private async Task<string?> PickFolderAsync(string title, string startIn)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return null;
        }

        IStorageFolder? start = Directory.Exists(startIn)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startIn)
            : null;
        IReadOnlyList<IStorageFolder> picked = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
