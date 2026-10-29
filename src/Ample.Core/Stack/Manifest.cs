using System.Text.Json;
using System.Text.Json.Serialization;
using Ample.Core;

namespace Ample.Stack;

/// <summary>One downloadable component in the manifest.</summary>
public sealed class ManifestComponent
{
    /// <summary>
    /// What the component is: "apache", "mod_fcgid", "php", "mariadb", "ledger", "phpmyadmin" or "vcredist". The
    /// installer decides from the id where the files go.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown to the user, e.g. "Apache HTTP Server".</summary>
    public string Name { get; set; } = "";

    /// <summary>The full version, e.g. "2.4.66"; PHP entries have one per installed version.</summary>
    public string Version { get; set; } = "";

    /// <summary>Absolute download URL of the zip (or the installer exe for "vcredist").</summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// SHA-256 of the file as 64 lowercase hex characters. Required for archives; null only for an installer whose
    /// publisher offers no stable checksum, which the installer logs as unverified.
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>The file's size in bytes when known, for the progress bar; 0 when unknown.</summary>
    public long Size { get; set; }

    /// <summary>True when the component is only installed on request (phpMyAdmin).</summary>
    public bool Optional { get; set; }

    /// <summary>For PHP: true on the version new projects use unless the user picks another.</summary>
    public bool Default { get; set; }

    /// <summary>The SPDX license id, listed in the LICENSES folder and the About page.</summary>
    public string License { get; set; } = "";

    /// <summary>Where the component comes from, for the About page.</summary>
    public string Homepage { get; set; } = "";

    /// <summary>The file name the download is saved under: the last segment of <see cref="Url"/>.</summary>
    [JsonIgnore]
    public string FileName
    {
        get
        {
            string lastSegment = Url.Split('/').LastOrDefault(segment => segment.Length > 0) ?? Id;
            int query = lastSegment.IndexOf('?');
            return query >= 0 ? lastSegment[..query] : lastSegment;
        }
    }

    /// <summary>True for the components installed once per version, side by side: PHP and MariaDB.</summary>
    [JsonIgnore]
    public bool IsVersioned => Id.ToLowerInvariant() is "php" or "mariadb";

    /// <summary>The id under which this component is recorded in stack.json: "php:8.4.14" for PHP,
    /// "mariadb:11.8.9" for MariaDB, else the id.</summary>
    [JsonIgnore]
    public string StackId => Id.ToLowerInvariant() switch
    {
        "php" => StackState.PhpId(Version),
        "mariadb" => StackState.MariaDbId(Version),
        _ => Id,
    };

    public override string ToString() => $"{Name} {Version}";
}

/// <summary>
/// The list of components AMPLE installs, with pinned versions and checksums. Published in the AMPLE repository and
/// read at install time; the offline bundle carries a copy next to the files it names.
/// </summary>
public sealed class Manifest
{
    /// <summary>The URL AMPLE reads the current manifest from.</summary>
    public const string DefaultUrl =
        "https://raw.githubusercontent.com/ClearanceClarence-Software/AMPLE/main/manifest/manifest.json";

    /// <summary>The manifest format version this build understands.</summary>
    public const int SupportedSchema = 1;

    /// <summary>The manifest format version.</summary>
    public int Schema { get; set; } = SupportedSchema;

    /// <summary>"stable" for now; room for "preview" later.</summary>
    public string Channel { get; set; } = "stable";

    /// <summary>When the manifest was last refreshed, as yyyy-MM-dd.</summary>
    public string Updated { get; set; } = "";

    /// <summary>The oldest AMPLE that can install this manifest, e.g. "0.1.0".</summary>
    public string MinAmpleVersion { get; set; } = "0.1.0";

    /// <summary>Every component, PHP once per version.</summary>
    public List<ManifestComponent> Components { get; set; } = [];

    /// <summary>The PHP versions in the manifest, newest first.</summary>
    [JsonIgnore]
    public List<ManifestComponent> PhpVersions => Components
        .Where(component => component.Id.Equals("php", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(component => VersionUtil.Parse(component.Version))
        .ToList();

    /// <summary>The MariaDB versions in the manifest, newest first.</summary>
    [JsonIgnore]
    public List<ManifestComponent> MariaDbVersions => Components
        .Where(component => component.Id.Equals("mariadb", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(component => VersionUtil.Parse(component.Version))
        .ToList();

    /// <summary>The default entry of a versioned component: the one marked default, else the newest.</summary>
    /// <param name="versions">The entries of one component, newest first.</param>
    /// <returns>The default; null when the list is empty.</returns>
    public static ManifestComponent? DefaultOf(List<ManifestComponent> versions) =>
        versions.FirstOrDefault(component => component.Default) ?? versions.FirstOrDefault();

    /// <summary>The one component with an id other than "php".</summary>
    /// <param name="id">The component id, matched case-insensitively.</param>
    /// <returns>The component; null when the manifest has none with that id.</returns>
    public ManifestComponent? Find(string id) =>
        Components.FirstOrDefault(component => component.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Checks the manifest is complete enough to install from.</summary>
    /// <returns>The problems found, one sentence each; empty when the manifest is usable.</returns>
    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Schema != SupportedSchema)
        {
            problems.Add($"Schema {Schema} is not supported; this AMPLE understands schema {SupportedSchema}.");
        }

        foreach (string required in new[] { "apache", "mod_fcgid", "ledger" })
        {
            if (Find(required) is null)
            {
                problems.Add($"The manifest has no \"{required}\" component.");
            }
        }
        foreach ((string name, List<ManifestComponent> versions) in new[]
                 {
                     ("PHP", PhpVersions), ("MariaDB", MariaDbVersions),
                 })
        {
            if (versions.Count == 0)
            {
                problems.Add($"The manifest has no {name} version.");
            }
            else if (versions.Count(component => component.Default) != 1)
            {
                problems.Add($"Exactly one {name} version must be marked as the default.");
            }
        }

        foreach (ManifestComponent component in Components)
        {
            string label = component.ToString();
            if (string.IsNullOrWhiteSpace(component.Id) || string.IsNullOrWhiteSpace(component.Version))
            {
                problems.Add($"A component is missing its id or version ({label}).");
            }
            if (!Uri.TryCreate(component.Url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add($"{label} needs an https URL.");
            }
            bool isInstaller = component.Id.Equals("vcredist", StringComparison.OrdinalIgnoreCase);
            bool hasValidHash = component.Sha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit);
            if (!hasValidHash && (!isInstaller || component.Sha256 is not null))
            {
                problems.Add($"{label} needs a 64-character SHA-256 checksum.");
            }
        }
        return problems;
    }

    /// <summary>Parses manifest text.</summary>
    /// <param name="json">The manifest as JSON.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="JsonException">The text is not a manifest.</exception>
    public static Manifest Parse(string json)
    {
        Manifest manifest = JsonSerializer.Deserialize(json, ManifestJson.Default.Manifest)
            ?? throw new JsonException("The manifest is the JSON literal null.");
        manifest.Components ??= [];
        return manifest;
    }

    /// <summary>The manifest as indented JSON, as the repository stores it.</summary>
    /// <returns>The JSON text with a final line break.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, ManifestJson.Default.Manifest) + "\n";

    /// <summary>
    /// A manifest on disk that wins over the published one: bundle\manifest.json (the offline installer) or a
    /// manifest.json next to AMPLE.exe (handy while developing).
    /// </summary>
    /// <param name="paths">The stack.</param>
    /// <returns>The first existing file; null when neither is there.</returns>
    public static string? LocalPath(AmplePaths paths) => AmplePaths.FirstExisting(
        Path.Combine(paths.Bundle, "manifest.json"),
        Path.Combine(paths.Root, "manifest.json"));

    /// <summary>Reads a manifest file.</summary>
    /// <param name="path">Full path of the file.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">The file is not a manifest.</exception>
    public static Manifest Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Downloads the current manifest.</summary>
    /// <param name="http">The client to use.</param>
    /// <param name="url">The manifest URL; null reads <see cref="DefaultUrl"/>.</param>
    /// <param name="ct">Cancels the request.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="HttpRequestException">The request fails.</exception>
    /// <exception cref="JsonException">The response is not a manifest.</exception>
    public static async Task<Manifest> FetchAsync(HttpClient http, string? url = null, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        return Parse(await http.GetStringAsync(url ?? DefaultUrl, timeout.Token));
    }
}

/// <summary>Source-generated JSON metadata for the manifest: camelCase keys, indented, nulls left out.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Manifest))]
internal sealed partial class ManifestJson : JsonSerializerContext;
