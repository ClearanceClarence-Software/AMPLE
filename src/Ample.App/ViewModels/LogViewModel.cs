using System.Collections.ObjectModel;
using System.Text;
using Ample.Core;

namespace Ample.App.ViewModels;

/// <summary>The Logs page: every line AMPLE wrote, with the server logs one click away.</summary>
public sealed class LogViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    /// <summary>Builds the page.</summary>
    /// <param name="main">The shell.</param>
    public LogViewModel(MainViewModel main)
    {
        _main = main;
        ClearCommand = new RelayCommand(() => _main.Log.Clear());
        OpenLogsFolderCommand = new RelayCommand(() => Shell.Open(_main.Paths.Logs));
        ToggleDebugCommand = new RelayCommand(() =>
        {
            _main.Settings.ShowDebug = !_main.Settings.ShowDebug;
            _main.Settings.Save();
            OnPropertyChanged(nameof(DebugLabel));
        });
    }

    /// <summary>The lines, oldest first.</summary>
    public ObservableCollection<LogEntry> Entries => _main.Log;

    /// <summary>The server logs for the quick-open row.</summary>
    public IReadOnlyList<FileLink> ServerLogs =>
        _main.Apache.LogFiles.Concat(_main.MariaDb.LogFiles).Where(link => File.Exists(link.Path)).ToList();

    /// <summary>"Hide debug lines" or "Show debug lines".</summary>
    public string DebugLabel => _main.Settings.ShowDebug ? "Hide debug lines" : "Show debug lines";

    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenLogsFolderCommand { get; }
    public RelayCommand ToggleDebugCommand { get; }

    /// <summary>Every line as text, for the clipboard.</summary>
    /// <returns>One line per entry, oldest first.</returns>
    public string AsText()
    {
        var text = new StringBuilder();
        foreach (LogEntry entry in Entries)
        {
            text.AppendLine(entry.AsText);
        }
        return text.ToString();
    }

    /// <summary>Re-reads the server log list, which changes as services start.</summary>
    public void Refresh() => OnPropertyChanged(nameof(ServerLogs));
}
