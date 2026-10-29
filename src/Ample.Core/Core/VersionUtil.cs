using System.Text.RegularExpressions;

namespace Ample.Core;

/// <summary>Finds and compares the dotted version numbers in feed entries, file names and labels.</summary>
public static partial class VersionUtil
{
    /// <summary>"8.4.14" / "11.4" / "MariaDB 11.4.5" → comparable Version (0.0 when unknown).</summary>
    /// <param name="text">Any text containing a dotted number; null or text without one gives 0.0.</param>
    /// <returns>The first dotted number in the text as a <see cref="Version"/>.</returns>
    public static Version Parse(string? text)
    {
        string? versionText = FromText(text);
        if (versionText is null)
        {
            return new Version(0, 0);
        }

        string[] parts = versionText.Split('.');
        return parts.Length == 1 ? new Version(int.Parse(parts[0]), 0) : Version.Parse(versionText);
    }

    /// <summary>Finds the first dotted number of two to four parts in a text.</summary>
    /// <param name="text">Any text, such as a file name or a version label; null or empty gives null.</param>
    /// <returns>The number as written, e.g. "8.4.14" from "php-8.4.14-nts-Win32-vs17-x64.zip"; null if none.</returns>
    public static string? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Match match = VersionNumber().Match(text);
        return match.Success ? match.Value : null;
    }

    /// <summary>"8.4" from "8.4.14".</summary>
    /// <param name="version">Any text containing a dotted number; null or text without one gives "0.0".</param>
    /// <returns>The major and minor parts joined by a dot.</returns>
    public static string MajorMinor(string? version)
    {
        Version parsed = Parse(version);
        return $"{parsed.Major}.{parsed.Minor}";
    }

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex VersionNumber();
}
