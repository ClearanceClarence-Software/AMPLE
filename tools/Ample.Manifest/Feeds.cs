using System.Text.Json;
using System.Text.RegularExpressions;
using Ample.Core;
using Ample.Stack;

namespace Ample.ManifestTool;

/// <summary>Reads the publishers' feeds to find the newest build of each component within its series.</summary>
public static partial class Feeds
{
    private const string PhpBase = "https://windows.php.net/downloads/releases/";
    private const string ApacheLoungeVs17 = "https://www.apachelounge.com/download/VS17/";
    private const string MariaDbApi = "https://downloads.mariadb.org/rest-api/mariadb/";
    private const string LedgerReleases = "https://api.github.com/repos/ClearanceClarence-Software/Ledger/releases/latest";
    private const string PhpMyAdminVersions = "https://www.phpmyadmin.net/home_page/version.json";

    /// <summary>The URL of the newest build in the component's series, e.g. the newest PHP 8.4.x.</summary>
    /// <param name="component">The component as the manifest has it now; its version names the series.</param>
    /// <returns>The URL; null when the component has no feed (vcredist) or the feed names nothing.</returns>
    public static async Task<string?> LatestUrlAsync(ManifestComponent component) => component.Id switch
    {
        "apache" => await ApacheLoungeAsync(HttpdZip(), "binaries/"),
        "mod_fcgid" => await ApacheLoungeAsync(FcgidZip(), "modules/"),
        "php" => await PhpAsync(VersionUtil.MajorMinor(component.Version)),
        "mariadb" => await MariaDbAsync(VersionUtil.MajorMinor(component.Version)),
        "ledger" => await LedgerAsync(),
        "phpmyadmin" => await PhpMyAdminAsync(),
        _ => null,
    };

    /// <summary>The version a component URL names.</summary>
    /// <param name="component">The component, for its id.</param>
    /// <param name="url">Its download URL.</param>
    /// <returns>The version from the file name; null when none is found.</returns>
    public static string? VersionFromUrl(ManifestComponent component, string url)
    {
        if (component.Id == "ledger")
        {
            Match tag = LedgerTag().Match(url);
            return tag.Success ? tag.Groups["v"].Value : null;
        }
        return VersionUtil.FromText(url.Split('/').Last());
    }

    private static async Task<string> GetAsync(string url)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await Downloads.Http.GetStringAsync(url, timeout.Token);
    }

    /// <summary>The newest file matching a pattern on the Apache Lounge VS17 page.</summary>
    private static async Task<string?> ApacheLoungeAsync(Regex pattern, string folder)
    {
        string page = await GetAsync(ApacheLoungeVs17);
        string? newest = pattern.Matches(page)
            .Select(match => match.Value)
            .Distinct()
            .OrderByDescending(VersionUtil.Parse)
            .ThenByDescending(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
        return newest is null ? null : ApacheLoungeVs17 + folder + newest;
    }

    /// <summary>The newest NTS x64 zip of one PHP branch from releases.json.</summary>
    private static async Task<string?> PhpAsync(string branch)
    {
        using JsonDocument document = JsonDocument.Parse(await GetAsync(PhpBase + "releases.json"));
        if (!document.RootElement.TryGetProperty(branch, out JsonElement branchElement))
        {
            return null;
        }

        foreach (JsonProperty build in branchElement.EnumerateObject())
        {
            bool isNtsX64 = build.Name.StartsWith("nts-", StringComparison.OrdinalIgnoreCase)
                && build.Name.EndsWith("-x64", StringComparison.OrdinalIgnoreCase);
            if (isNtsX64
                && build.Value.ValueKind == JsonValueKind.Object
                && build.Value.TryGetProperty("zip", out JsonElement zip)
                && zip.TryGetProperty("path", out JsonElement path)
                && path.GetString() is { Length: > 0 } zipPath)
            {
                return PhpBase + zipPath;
            }
        }
        return null;
    }

    /// <summary>The newest Windows x64 zip of one MariaDB series.</summary>
    private static async Task<string?> MariaDbAsync(string series)
    {
        using JsonDocument document = JsonDocument.Parse(await GetAsync($"{MariaDbApi}{series}/latest/"));
        return FindWindowsZip(document.RootElement);
    }

    /// <summary>Searches a JSON tree depth first for the first winx64 zip file entry.</summary>
    private static string? FindWindowsZip(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(FindWindowsZip).FirstOrDefault(url => url is not null);
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("file_name", out JsonElement fileName)
            && fileName.GetString() is { } name
            && MariaDbZip().Match(name) is { Success: true } match)
        {
            // The feed's own download link points at a plain-http mirror; the archive serves every release over https
            // at a predictable path, so the manifest always uses that.
            string version = match.Groups["v"].Value;
            return $"https://archive.mariadb.org/mariadb-{version}/winx64-packages/{name}";
        }

        return element.EnumerateObject()
            .Select(property => FindWindowsZip(property.Value))
            .FirstOrDefault(url => url is not null);
    }

    /// <summary>The source zip of Ledger's latest GitHub release.</summary>
    private static async Task<string?> LedgerAsync()
    {
        using JsonDocument document = JsonDocument.Parse(await GetAsync(LedgerReleases));
        string? tag = document.RootElement.TryGetProperty("tag_name", out JsonElement tagName)
            ? tagName.GetString()
            : null;
        return tag is null
            ? null
            : $"https://github.com/ClearanceClarence-Software/Ledger/archive/refs/tags/{tag}.zip";
    }

    /// <summary>The all-languages zip of the current phpMyAdmin release.</summary>
    private static async Task<string?> PhpMyAdminAsync()
    {
        using JsonDocument document = JsonDocument.Parse(await GetAsync(PhpMyAdminVersions));
        string? version = document.RootElement.TryGetProperty("version", out JsonElement current)
            ? current.GetString()
            : null;
        return version is null
            ? null
            : $"https://files.phpmyadmin.net/phpMyAdmin/{version}/phpMyAdmin-{version}-all-languages.zip";
    }

    [GeneratedRegex(@"httpd-\d+\.\d+\.\d+-\d+-win64-VS17\.zip", RegexOptions.IgnoreCase)]
    private static partial Regex HttpdZip();

    [GeneratedRegex(@"mod_fcgid-\d+\.\d+\.\d+-win64-VS17\.zip", RegexOptions.IgnoreCase)]
    private static partial Regex FcgidZip();

    [GeneratedRegex(@"^mariadb-(?<v>\d+\.\d+\.\d+)-winx64\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex MariaDbZip();

    [GeneratedRegex(@"/tags/v?(?<v>[^/]+)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex LedgerTag();
}
