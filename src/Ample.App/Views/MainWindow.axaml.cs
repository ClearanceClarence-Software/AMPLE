using Ample.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Ample.App.Views;

/// <summary>The shell window: sidebar plus the current page.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppIcon.Apply(this);
        Closing += OnClosing;
    }

    /// <summary>The tray icon, set by the app; closing hides to it when the setting is on.</summary>
    public TrayController? Tray { get; set; }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>Hides the window instead of closing when AMPLE should keep running in the tray.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (Tray is not null && ViewModel?.Settings.KeepInTray == true && !Tray.IsQuitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Posted, so the shutdown doesn't close this window again from inside its own Closing handler.
        Dispatcher.UIThread.Post(() => Tray?.Quit());
    }
}
