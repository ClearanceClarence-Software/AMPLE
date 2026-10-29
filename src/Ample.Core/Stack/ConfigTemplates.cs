using System.Reflection;
using System.Text;

namespace Ample.Stack;

/// <summary>The config templates embedded in Ample.Core, and the ${TOKEN} substitution that renders them.</summary>
public static class ConfigTemplates
{
    private static readonly Assembly _assembly = typeof(ConfigTemplates).Assembly;

    /// <summary>Reads a template by its path under Templates, e.g. "apache/httpd.conf".</summary>
    /// <param name="name">The path with forward slashes.</param>
    /// <returns>The template text.</returns>
    /// <exception cref="FileNotFoundException">No template has that name.</exception>
    public static string Read(string name)
    {
        // The build names resources "Templates/<folder>/<file>" with the OS separator, so both forms are tried.
        string wanted = "Templates/" + name;
        string? resourceName = _assembly.GetManifestResourceNames()
            .FirstOrDefault(
                candidate => candidate.Replace('\\', '/').Equals(wanted, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new FileNotFoundException($"Ample.Core has no template named \"{name}\".");
        }

        using Stream stream = _assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Replaces every ${TOKEN} in a text with its value.</summary>
    /// <param name="template">The text with tokens.</param>
    /// <param name="values">Token names without the ${} and their values.</param>
    /// <returns>The text with every listed token replaced; unlisted tokens stay as written.</returns>
    public static string RenderText(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = new StringBuilder(template);
        foreach ((string token, string value) in values)
        {
            rendered.Replace("${" + token + "}", value);
        }
        return rendered.ToString();
    }

    /// <summary>Reads and renders a template in one step.</summary>
    /// <param name="name">The template's path under Templates.</param>
    /// <param name="values">Token names and their values.</param>
    /// <returns>The rendered text.</returns>
    public static string Render(string name, IReadOnlyDictionary<string, string> values) =>
        RenderText(Read(name), values);

    /// <summary>Writes a text file as UTF-8 without a BOM, with Windows line endings, creating its folder.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <param name="text">The text; its line breaks are normalized to CRLF.</param>
    public static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string normalized = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        File.WriteAllText(path, normalized, new UTF8Encoding(false));
    }
}
