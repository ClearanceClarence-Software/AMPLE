using Ample.App.ViewModels;
using Ample.App.Views;
using Ample.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Ample.App;

/// <summary>The Avalonia application: loads the settings, finds the stack and opens the main window.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // --root C:\ample manages that folder instead of the exe's own; handy when running from the IDE.
            AppSettings settings = AppSettings.Load();
            string? rootArgument = RootFromArguments(desktop.Args ?? []);
            var viewModel = new MainViewModel(settings, rootArgument);
            ThemeSwitcher.Apply(viewModel.Settings.Theme);
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
                WindowState = viewModel.Settings.StartMinimized ? WindowState.Minimized : WindowState.Normal,
            };
            desktop.MainWindow = mainWindow;

            // Closing the window keeps AMPLE in the tray when that setting is on; Quit in the tray menu shuts down.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var tray = new TrayController(this, mainWindow, viewModel);
            mainWindow.Tray = tray;
            desktop.ShutdownRequested += (_, _) =>
            {
                tray.Dispose();
                viewModel.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The folder after --root on the command line, in either "--root X" or "--root=X" form.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The folder as typed; null when the switch is absent.</returns>
    private static string? RootFromArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--root=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i]["--root=".Length..].Trim('"');
            }
            if (args[i].Equals("--root", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
