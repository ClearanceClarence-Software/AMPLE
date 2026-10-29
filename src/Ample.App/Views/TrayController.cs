using System.ComponentModel;
using Ample.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Ample.App.Views;

/// <summary>
/// The notification area icon. Its apex node shows the stack's state (green running, amber attention, gray
/// stopped), and its menu starts and stops the stack, opens the database tool and quits.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly Application _application;
    private readonly Window _window;
    private readonly MainViewModel _viewModel;
    private readonly TrayIcon _trayIcon;
    private string _state = "";

    /// <summary>Adds the icon to the notification area.</summary>
    /// <param name="application">The app the icon belongs to.</param>
    /// <param name="window">The main window, shown again from the icon.</param>
    /// <param name="viewModel">Supplies the services; the icon follows their state.</param>
    public TrayController(Application application, Window window, MainViewModel viewModel)
    {
        _application = application;
        _window = window;
        _viewModel = viewModel;
        _trayIcon = new TrayIcon { ToolTipText = BuildInfo.AppName };
        _trayIcon.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });

        foreach (ServiceViewModel service in viewModel.Services)
        {
            service.PropertyChanged += OnServiceChanged;
        }
        viewModel.PropertyChanged += OnMainChanged;
        RebuildMenu();
        UpdateIcon();
    }

    /// <summary>True once Quit was chosen, so the window's Closing handler lets the app shut down.</summary>
    public bool IsQuitting { get; private set; }

    /// <summary>Shows the main window, restored and in front.</summary>
    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
    }

    /// <summary>Stops the services when the user wants that on exit, then shuts the app down.</summary>
    public void Quit()
    {
        if (IsQuitting)
        {
            return;
        }

        IsQuitting = true;
        if (_application.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Removes the icon and stops following the services.</summary>
    public void Dispose()
    {
        foreach (ServiceViewModel service in _viewModel.Services)
        {
            service.PropertyChanged -= OnServiceChanged;
        }
        _viewModel.PropertyChanged -= OnMainChanged;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ServiceViewModel.IsRunning) or nameof(ServiceViewModel.HasConflict)
            or nameof(ServiceViewModel.IsBusy) or nameof(ServiceViewModel.ActionLabel))
        {
            UpdateIcon();
            RebuildMenu();
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsSetupNeeded))
        {
            RebuildMenu();
        }
    }

    /// <summary>Picks the icon for the overall state.</summary>
    private void UpdateIcon()
    {
        bool anyConflict = _viewModel.Services.Any(service => service.HasConflict);
        int running = _viewModel.Services.Count(service => service.IsRunning);
        string state = anyConflict
            ? "attention"
            : running == _viewModel.Services.Count ? "running" : running > 0 ? "attention" : "stopped";
        if (state == _state)
        {
            return;
        }

        _state = state;
        _trayIcon.Icon = AppIcon.Tray(state);
        _trayIcon.ToolTipText = state switch
        {
            "running" => "AMPLE · everything is running",
            "attention" => "AMPLE · needs attention",
            _ => "AMPLE · stopped",
        };
    }

    /// <summary>Builds the menu: Show, start/stop per service, Start all / Stop all, Open database tool,
    /// Quit.</summary>
    private void RebuildMenu()
    {
        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("Show AMPLE");
        showItem.Click += (_, _) => ShowWindow();
        menu.Add(showItem);
        menu.Add(new NativeMenuItemSeparator());

        if (!_viewModel.IsSetupNeeded)
        {
            foreach (ServiceViewModel service in _viewModel.Services)
            {
                var item = new NativeMenuItem($"{service.ActionLabel} {service.Name.ToLowerInvariant()}")
                {
                    IsEnabled = service.ActionCommand.CanExecute(null),
                };
                item.Click += (_, _) => service.ActionCommand.Execute(null);
                menu.Add(item);
            }
            menu.Add(new NativeMenuItemSeparator());

            var startAll = new NativeMenuItem("Start all");
            startAll.Click += (_, _) => _viewModel.StartAllCommand.Execute(null);
            menu.Add(startAll);
            var stopAll = new NativeMenuItem("Stop all");
            stopAll.Click += (_, _) => _viewModel.StopAllCommand.Execute(null);
            menu.Add(stopAll);
            menu.Add(new NativeMenuItemSeparator());

            var openLocalhost = new NativeMenuItem("Open localhost");
            openLocalhost.Click += (_, _) => _viewModel.OpenLocalhostCommand.Execute(null);
            menu.Add(openLocalhost);
            var openTool = new NativeMenuItem(_viewModel.Overview.ToolOpenLabel);
            openTool.Click += (_, _) => _viewModel.OpenDatabaseTool();
            menu.Add(openTool);
            menu.Add(new NativeMenuItemSeparator());
        }

        var quitItem = new NativeMenuItem("Quit AMPLE");
        quitItem.Click += (_, _) => Quit();
        menu.Add(quitItem);
        _trayIcon.Menu = menu;
    }
}
