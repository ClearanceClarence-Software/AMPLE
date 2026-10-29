using Avalonia.Controls;
using Avalonia.Platform;

namespace Ample.App;

/// <summary>The AMPLE icons in Assets: the app icon and the three tray states.</summary>
public static class AppIcon
{
    private static readonly Uri _appIcon = new("avares://AMPLE/Assets/ample.ico");
    private static readonly Uri _trayRunning = new("avares://AMPLE/Assets/tray-running.ico");
    private static readonly Uri _trayAttention = new("avares://AMPLE/Assets/tray-attention.ico");
    private static readonly Uri _trayStopped = new("avares://AMPLE/Assets/tray-stopped.ico");

    /// <summary>Sets a window's title bar and taskbar icon; unchanged when the asset is missing.</summary>
    /// <param name="window">The window.</param>
    public static void Apply(Window window)
    {
        if (Load(_appIcon) is { } icon)
        {
            window.Icon = icon;
        }
    }

    /// <summary>The tray icon for a state: the apex node green, amber or gray.</summary>
    /// <param name="state">"running", "attention" or anything else for stopped.</param>
    /// <returns>The icon; the app icon when the state's file is missing; null when nothing loads.</returns>
    public static WindowIcon? Tray(string state)
    {
        Uri uri = state switch
        {
            "running" => _trayRunning,
            "attention" => _trayAttention,
            _ => _trayStopped,
        };
        return Load(uri) ?? Load(_appIcon);
    }

    private static WindowIcon? Load(Uri uri)
    {
        try
        {
            return AssetLoader.Exists(uri) ? new WindowIcon(AssetLoader.Open(uri)) : null;
        }
        catch
        {
            // A missing or undecodable icon is cosmetic: the window keeps the default one.
            return null;
        }
    }
}
