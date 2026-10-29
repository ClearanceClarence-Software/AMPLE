using System.Collections.Specialized;
using Ample.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ample.App.Views;

/// <summary>Code-behind for Logs: copies the log and keeps the newest line in view.</summary>
public partial class LogView : UserControl
{
    private LogViewModel? _attached;

    public LogView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_attached is not null)
        {
            _attached.Entries.CollectionChanged -= OnEntriesChanged;
        }
        _attached = DataContext as LogViewModel;
        if (_attached is not null)
        {
            _attached.Entries.CollectionChanged += OnEntriesChanged;
            _attached.Refresh();
            LogScroll.ScrollToEnd();
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            LogScroll.ScrollToEnd();
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (_attached is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(_attached.AsText());
    }
}
