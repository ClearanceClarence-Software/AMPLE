namespace Ample.Core;

/// <summary>The names of AMPLE's color themes, as stored in settings.json. The app applies them.</summary>
public static class AppTheme
{
    /// <summary>Follows the Windows setting (Settings › Personalization › Colors) and changes with it.</summary>
    public const string System = "System";

    /// <summary>Always light.</summary>
    public const string Light = "Light";

    /// <summary>Always dark.</summary>
    public const string Dark = "Dark";

    /// <summary>A stored theme name made safe to use.</summary>
    /// <param name="theme">The value from settings.json; null, empty or unknown values mean System.</param>
    /// <returns><see cref="Light"/>, <see cref="Dark"/> or <see cref="System"/>.</returns>
    public static string Normalize(string? theme)
    {
        if (string.Equals(theme, Light, StringComparison.OrdinalIgnoreCase))
        {
            return Light;
        }
        return string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : System;
    }
}
