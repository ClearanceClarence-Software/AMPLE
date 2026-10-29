using Ample.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ample.App.Views;

/// <summary>Code-behind for Settings: the clipboard button.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private async void CopyPassword_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { RootPassword: { } password }
            || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(password);
    }
}
