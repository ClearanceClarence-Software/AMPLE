using Ample.Core;

namespace Ample.App.ViewModels;

/// <summary>A config or log file in a service's menu: a label and the command that opens it.</summary>
/// <param name="label">What the menu shows.</param>
/// <param name="path">Full path of the file or folder.</param>
/// <param name="editor">The configured editor, or null for Notepad.</param>
public sealed class FileLink(string label, string path, Func<string?> editor)
{
    /// <summary>What the menu shows.</summary>
    public string Label { get; } = label;

    /// <summary>Full path of the file or folder.</summary>
    public string Path { get; } = path;

    /// <summary>Opens a folder in Explorer and a file in the editor.</summary>
    public RelayCommand OpenCommand { get; } = new(() =>
    {
        if (Directory.Exists(path))
        {
            Shell.Open(path);
        }
        else
        {
            Shell.OpenInEditor(path, editor());
        }
    });
}
