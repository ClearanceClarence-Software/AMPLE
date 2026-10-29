using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ample.Stack;

/// <summary>One installed component, as recorded in stack.json.</summary>
public sealed class InstalledComponent
{
    /// <summary>The version installed, as the manifest named it, e.g. "2.4.66" or "8.4.14".</summary>
    public string Version { get; set; } = "";

    /// <summary>The SHA-256 of the archive it was unpacked from, lowercase hex.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>"download" or "bundle": where the archive came from.</summary>
    public string Source { get; set; } = "download";

    /// <summary>When it was unpacked.</summary>
    public DateTimeOffset InstalledAt { get; set; }
}

/// <summary>
/// What is installed in an AMPLE folder: etc\ample\stack.json. The folder describes itself, so AMPLE.exe can be
/// replaced or the folder moved and the state is still known.
/// </summary>
public sealed class StackState
{
    /// <summary>The file format version, for future migrations.</summary>
    public int Schema { get; set; } = 1;

    /// <summary>The root the config files were rendered for; when it differs from the actual root, the stack
    /// moved.</summary>
    public string? RenderedRoot { get; set; }

    /// <summary>The manifest's "updated" stamp the components were installed from.</summary>
    public string? ManifestUpdated { get; set; }

    /// <summary>The manifest's channel, e.g. "stable".</summary>
    public string? ManifestChannel { get; set; }

    /// <summary>
    /// Installed components by id: "apache", "mod_fcgid", "ledger", "phpmyadmin", "php:8.4.27" per PHP version and
    /// "mariadb:11.8.9" per MariaDB version.
    /// </summary>
    public Dictionary<string, InstalledComponent> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The PHP version new projects use, as a folder name under bin\php.</summary>
    public string? DefaultPhp { get; set; }

    /// <summary>The MariaDB version that runs, as a folder name under bin\mariadb; the others wait their
    /// turn.</summary>
    public string? ActiveMariaDb { get; set; }

    /// <summary>
    /// The MariaDB root password AMPLE generated at install. Stored in plain text in the stack folder, like every
    /// local development stack's credentials; only meant for a database that listens on this computer.
    /// </summary>
    public string? MariaDbRootPassword { get; set; }

    /// <summary>When the stack was first installed.</summary>
    public DateTimeOffset? InstalledAt { get; set; }

    /// <summary>The id under which a PHP version is recorded in <see cref="Components"/>.</summary>
    /// <param name="version">The full PHP version.</param>
    /// <returns>"php:" followed by the version.</returns>
    public static string PhpId(string version) => "php:" + version;

    /// <summary>The id under which a MariaDB version is recorded in <see cref="Components"/>.</summary>
    /// <param name="version">The full MariaDB version.</param>
    /// <returns>"mariadb:" followed by the version.</returns>
    public static string MariaDbId(string version) => "mariadb:" + version;

    /// <summary>True when the component is recorded as installed.</summary>
    /// <param name="id">The component id, e.g. "mariadb".</param>
    /// <returns>True when <see cref="Components"/> has the id.</returns>
    public bool Has(string id) => Components.ContainsKey(id);

    /// <summary>Reads stack.json.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <returns>The state; a fresh one when the file is missing or unreadable.</returns>
    public static StackState Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new StackState();
            }

            StackState state = JsonSerializer.Deserialize(File.ReadAllText(path), StackJson.Default.StackState)
                ?? new StackState();
            state.Components ??= new Dictionary<string, InstalledComponent>(StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged stack.json means the installer sees nothing installed and offers a repair.
            return new StackState();
        }
    }

    /// <summary>Writes stack.json, creating its folder when needed.</summary>
    /// <param name="path">Full path of the file.</param>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, StackJson.Default.StackState));
    }
}

/// <summary>Source-generated JSON metadata for stack.json.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StackState))]
internal sealed partial class StackJson : JsonSerializerContext;
