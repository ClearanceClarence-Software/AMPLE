using Ample.Core;
using Avalonia;
using Avalonia.Styling;

namespace Ample.App;

/// <summary>Applies one of <see cref="AppTheme"/>'s names to the running app.</summary>
public static class ThemeSwitcher
{
    /// <summary>Switches the whole app to a theme; open windows repaint at once.</summary>
    /// <param name="theme">One of the theme names; anything else follows Windows.</param>
    public static void Apply(string? theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = AppTheme.Normalize(theme) switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
