using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ample.Core;

/// <summary>
/// The folders of one AMPLE installation. AMPLE.exe sits in the root, so the root is wherever the exe runs from
/// unless the settings name another folder.
/// </summary>
/// <param name="root">The installation folder, without a trailing separator.</param>
public sealed partial class AmplePaths(string root)
{
    /// <summary>The installation folder, e.g. C:\ample.</summary>
    public string Root { get; } = root.TrimEnd('\\', '/');

    /// <summary>bin: the components as unpacked from their official zips.</summary>
    public string Bin => Path.Combine(Root, "bin");

    /// <summary>bin\apache: Apache httpd (Apache Lounge build). Its config lives in <see cref="EtcApache"/>.</summary>
    public string Apache => Path.Combine(Bin, "apache");

    /// <summary>bin\apache\bin\httpd.exe.</summary>
    public string HttpdExe => Path.Combine(Apache, "bin", "httpd.exe");

    /// <summary>bin\php: one subfolder per installed PHP version, named by its full version ("8.4.14").</summary>
    public string PhpRoot => Path.Combine(Bin, "php");

    /// <summary>bin\php\&lt;version&gt;.</summary>
    /// <param name="version">The full PHP version the folder is named after.</param>
    /// <returns>The folder; it may not exist.</returns>
    public string Php(string version) => Path.Combine(PhpRoot, version);

    /// <summary>bin\mariadb: one subfolder per installed MariaDB version, named by its full version
    /// ("11.8.9").</summary>
    public string MariaDbRoot => Path.Combine(Bin, "mariadb");

    /// <summary>bin\mariadb\&lt;version&gt;: that version's server and tools. Its data lives in
    /// <see cref="DataMariaDb"/>.</summary>
    /// <param name="version">The full MariaDB version the folder is named after.</param>
    /// <returns>The folder; it may not exist.</returns>
    public string MariaDb(string version) => Path.Combine(MariaDbRoot, version);

    /// <summary>bin\mariadb\&lt;version&gt;\bin.</summary>
    /// <param name="version">The full MariaDB version.</param>
    /// <returns>The bin folder; it may not exist.</returns>
    public string MariaDbBin(string version) => Path.Combine(MariaDb(version), "bin");

    /// <summary>apps: the web apps AMPLE ships, served outside the projects.</summary>
    public string Apps => Path.Combine(Root, "apps");

    /// <summary>apps\ledger: the Ledger database tool.</summary>
    public string Ledger => Path.Combine(Apps, "ledger");

    /// <summary>apps\phpmyadmin: phpMyAdmin, when installed.</summary>
    public string PhpMyAdmin => Path.Combine(Apps, "phpmyadmin");

    /// <summary>etc: every config file AMPLE writes or the user edits.</summary>
    public string Etc => Path.Combine(Root, "etc");

    /// <summary>etc\apache: httpd.conf and the files it includes.</summary>
    public string EtcApache => Path.Combine(Etc, "apache");

    /// <summary>etc\apache\httpd.conf.</summary>
    public string HttpdConf => Path.Combine(EtcApache, "httpd.conf");

    /// <summary>etc\apache\projects.conf: the virtual hosts AMPLE writes for the projects.</summary>
    public string ProjectsConf => Path.Combine(EtcApache, "projects.conf");

    /// <summary>etc\apache\custom: the user's own .conf files, included after AMPLE's.</summary>
    public string EtcApacheCustom => Path.Combine(EtcApache, "custom");

    /// <summary>etc\apache\ssl: the local certificate Apache serves.</summary>
    public string EtcApacheSsl => Path.Combine(EtcApache, "ssl");

    /// <summary>etc\mariadb\my.ini.</summary>
    public string MyIni => Path.Combine(Etc, "mariadb", "my.ini");

    /// <summary>etc\ample: AMPLE's own records about this installation.</summary>
    public string EtcAmple => Path.Combine(Etc, "ample");

    /// <summary>etc\ample\stack.json: which components are installed, from which manifest, and for which
    /// root.</summary>
    public string StackFile => Path.Combine(EtcAmple, "stack.json");

    /// <summary>data\mariadb\&lt;version&gt;: that version's database files. Each version keeps its own.</summary>
    /// <param name="version">The full MariaDB version.</param>
    /// <returns>The data folder; it may not exist.</returns>
    public string DataMariaDb(string version) => Path.Combine(Root, "data", "mariadb", version);

    /// <summary>etc\php: the projects' custom php.ini files, one per project that has one.</summary>
    public string EtcPhp => Path.Combine(Etc, "php");

    /// <summary>www\_ample: the pages AMPLE itself serves on localhost (welcome page data, phpinfo per
    /// version).</summary>
    public string WwwAmple => Path.Combine(Www, "_ample");

    /// <summary>logs: one subfolder per component.</summary>
    public string Logs => Path.Combine(Root, "logs");

    /// <summary>logs\apache.</summary>
    public string LogsApache => Path.Combine(Logs, "apache");

    /// <summary>logs\mariadb.</summary>
    public string LogsMariaDb => Path.Combine(Logs, "mariadb");

    /// <summary>logs\php.</summary>
    public string LogsPhp => Path.Combine(Logs, "php");

    /// <summary>projects: the default parent for new projects' folders.</summary>
    public string Projects => Path.Combine(Root, "projects");

    /// <summary>www: what http://localhost serves, with AMPLE's welcome page.</summary>
    public string Www => Path.Combine(Root, "www");

    /// <summary>tmp: downloads and staging folders; safe to empty when nothing is installing.</summary>
    public string Tmp => Path.Combine(Root, "tmp");

    /// <summary>bundle: components shipped with the offline installer, looked up before anything is
    /// downloaded.</summary>
    public string Bundle => Path.Combine(Root, "bundle");

    /// <summary>The root in the form Apache and PHP configs use: forward slashes, no trailing slash.</summary>
    public string RootForConfig => ConfigPath(Root);

    /// <summary>Every folder that must exist for the stack to run; created by the installer and on each
    /// start.</summary>
    public IEnumerable<string> RequiredFolders =>
    [
        Bin, PhpRoot, MariaDbRoot, Apps, EtcApache, EtcApacheCustom, EtcApacheSsl, Path.GetDirectoryName(MyIni)!,
        EtcAmple, EtcPhp, Path.Combine(Root, "data", "mariadb"), LogsApache, LogsMariaDb, LogsPhp, Projects, Www,
        WwwAmple, Tmp,
    ];

    /// <summary>Creates every folder in <see cref="RequiredFolders"/> that is missing.</summary>
    public void EnsureFolders()
    {
        foreach (string folder in RequiredFolders)
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>The installed PHP versions, newest first, from the folders under bin\php that hold
    /// php-cgi.exe.</summary>
    /// <returns>The folder names, which are full versions such as "8.4.14".</returns>
    public List<string> InstalledPhpVersions()
    {
        if (!Directory.Exists(PhpRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(PhpRoot)
            .Where(folder => File.Exists(Path.Combine(folder, "php-cgi.exe")))
            .Select(folder => Path.GetFileName(folder))
            .OrderByDescending(VersionUtil.Parse)
            .ToList();
    }

    /// <summary>The installed MariaDB versions, newest first, from the folders under bin\mariadb with a server
    /// exe.</summary>
    /// <returns>The folder names, which are full versions such as "11.8.9".</returns>
    public List<string> InstalledMariaDbVersions()
    {
        if (!Directory.Exists(MariaDbRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(MariaDbRoot)
            .Where(folder => File.Exists(Path.Combine(folder, "bin", "mariadbd.exe"))
                || File.Exists(Path.Combine(folder, "bin", "mysqld.exe")))
            .Select(folder => Path.GetFileName(folder))
            .OrderByDescending(VersionUtil.Parse)
            .ToList();
    }

    /// <summary>True when the folder holds an AMPLE stack: its stack.json exists.</summary>
    /// <param name="folder">The folder to test; a missing folder gives false.</param>
    /// <returns>True when etc\ample\stack.json is there.</returns>
    public static bool IsAmpleRoot(string folder) => File.Exists(new AmplePaths(folder).StackFile);

    /// <summary>The root AMPLE runs from: the configured one when it is set, otherwise the exe's folder.</summary>
    /// <param name="configuredRoot">The folder from the settings; null or blank means "where the exe is".</param>
    /// <returns>The paths of that root.</returns>
    public static AmplePaths Resolve(string? configuredRoot)
    {
        string root = string.IsNullOrWhiteSpace(configuredRoot)
            ? AppContext.BaseDirectory
            : configuredRoot;
        return new AmplePaths(Path.GetFullPath(root));
    }

    /// <summary>A Windows path in the form Apache and PHP configs use: forward slashes, no trailing slash.</summary>
    /// <param name="path">A full Windows path.</param>
    /// <returns>The path with backslashes turned into forward slashes.</returns>
    public static string ConfigPath(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>"2.4.66", "11.4.5" etc. from an exe's version resource, or null.</summary>
    /// <param name="exePath">Full path of the executable; null or a missing file gives null.</param>
    /// <returns>
    /// The leading dotted number (up to three parts) of the product version, or of the file version when the
    /// product version is blank; the whole trimmed text when it does not start with a number; null when there is
    /// no file, no version or it cannot be read.
    /// </returns>
    public static string? FileVersion(string? exePath)
    {
        if (exePath is null || !File.Exists(exePath))
        {
            return null;
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            string? rawVersion = !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                ? versionInfo.ProductVersion
                : versionInfo.FileVersion;
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            string dottedVersion = rawVersion.Replace(", ", ".", StringComparison.Ordinal).Replace(',', '.');
            Match match = VersionPattern().Match(dottedVersion);
            return match.Success ? match.Value : dottedVersion.Trim();
        }
        catch
        {
            // Reading another program's version resource may fail in many ways; the version is informational,
            // so it shows as unknown.
            return null;
        }
    }

    /// <summary>The first of the given files that exists.</summary>
    /// <param name="paths">Full file paths, tried in order.</param>
    /// <returns>The first existing path; null when none exists.</returns>
    public static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    [GeneratedRegex(@"^\d+(\.\d+){0,2}")]
    private static partial Regex VersionPattern();
}
