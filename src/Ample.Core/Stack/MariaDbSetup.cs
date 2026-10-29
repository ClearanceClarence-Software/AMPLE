using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ample.Core;

namespace Ample.Stack;

/// <summary>
/// Writes my.ini for the active MariaDB version, creates each version's data folder with the root password, and
/// re-points my.ini when the active version or the stack folder changes.
/// </summary>
public static partial class MariaDbSetup
{
    /// <summary>Writes etc\mariadb\my.ini from the template unless the user already has one.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The active MariaDB version the paths point at.</param>
    /// <param name="ports">The ports from the settings.</param>
    /// <returns>True when the file was written; false when it existed.</returns>
    public static bool WriteIniIfMissing(AmplePaths paths, string version, PortSettings ports)
    {
        if (File.Exists(paths.MyIni))
        {
            return false;
        }

        string text = ConfigTemplates.Render("mariadb/my.ini", new Dictionary<string, string>
        {
            ["AMPLE_ROOT"] = paths.RootForConfig,
            ["MARIADB_VERSION"] = version,
            ["MARIADB_PORT"] = ports.MariaDb.ToString(),
        });
        ConfigTemplates.WriteFile(paths.MyIni, text);
        return true;
    }

    /// <summary>Points an existing my.ini at one version's binaries and data folder.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The version to activate.</param>
    public static void SetActiveVersion(AmplePaths paths, string version)
    {
        if (!File.Exists(paths.MyIni))
        {
            return;
        }

        string text = FileTools.ReadText(paths.MyIni, out var encoding);
        string root = paths.RootForConfig;
        string updated = BaseDirLine().Replace(text, $"${{key}}\"{root}/bin/mariadb/{version}\"", 1);
        updated = DataDirLine().Replace(updated, $"${{key}}\"{root}/data/mariadb/{version}\"", 1);
        updated = PidFileLine().Replace(updated, $"${{key}}\"{root}/data/mariadb/{version}/mariadb.pid\"", 1);
        File.WriteAllText(paths.MyIni, updated, encoding);
    }

    /// <summary>Changes the port lines of an existing my.ini.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="port">The new port.</param>
    public static void SetPort(AmplePaths paths, int port)
    {
        if (!File.Exists(paths.MyIni))
        {
            return;
        }

        string text = FileTools.ReadText(paths.MyIni, out var encoding);
        string updated = PortLine().Replace(text, $"${{key}}={port}");
        File.WriteAllText(paths.MyIni, updated, encoding);
    }

    /// <summary>Replaces the old root path with the new one in my.ini after the stack moved.</summary>
    /// <param name="paths">The stack at its new root.</param>
    /// <param name="oldRoot">The root the file was written for.</param>
    public static void Relocate(AmplePaths paths, string oldRoot)
    {
        if (!File.Exists(paths.MyIni))
        {
            return;
        }

        string text = FileTools.ReadText(paths.MyIni, out var encoding);
        string updated = text
            .Replace(AmplePaths.ConfigPath(oldRoot), paths.RootForConfig, StringComparison.OrdinalIgnoreCase)
            .Replace(oldRoot.TrimEnd('\\', '/'), paths.Root, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(paths.MyIni, updated, encoding);
    }

    /// <summary>True when a version's data folder already holds a system database.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The MariaDB version.</param>
    /// <returns>True when data\mariadb\&lt;version&gt;\mysql exists.</returns>
    public static bool IsInitialized(AmplePaths paths, string version) =>
        Directory.Exists(Path.Combine(paths.DataMariaDb(version), "mysql"));

    /// <summary>A new root password: 20 characters from an alphabet without look-alikes or shell specials.</summary>
    /// <returns>The password.</returns>
    public static string NewPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        return RandomNumberGenerator.GetString(alphabet, 20);
    }

    /// <summary>
    /// Creates the system databases for one version in its data folder with mariadb-install-db.exe and sets the
    /// root password.
    /// </summary>
    /// <param name="paths">The stack; the data folder is created when missing.</param>
    /// <param name="version">The MariaDB version whose tools run and whose data folder is filled.</param>
    /// <param name="rootPassword">The password root gets.</param>
    /// <param name="port">The port written into the data folder's own my.ini, which the server ignores.</param>
    /// <returns>The tool's result; exit code 0 when the data folder is ready.</returns>
    public static async Task<Shell.RunResult> InitializeAsync(
        AmplePaths paths,
        string version,
        string rootPassword,
        int port)
    {
        string bin = paths.MariaDbBin(version);
        string? installDb = AmplePaths.FirstExisting(
            Path.Combine(bin, "mariadb-install-db.exe"),
            Path.Combine(bin, "mysql_install_db.exe"));
        if (installDb is null)
        {
            return new Shell.RunResult(-1, "", $@"No mariadb-install-db.exe in bin\mariadb\{version}\bin", false);
        }

        string dataFolder = paths.DataMariaDb(version);
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(paths.LogsMariaDb);

        // The tool writes a my.ini of its own into the data folder; the server runs with etc\mariadb\my.ini instead.
        string arguments = $"--datadir=\"{dataFolder}\" --password=\"{rootPassword}\" --port={port}";
        return await Shell.RunAsync(installDb, arguments, bin, TimeSpan.FromMinutes(3));
    }

    [GeneratedRegex(@"^(?<key>port)=\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PortLine();

    [GeneratedRegex(@"^(?<key>basedir=)""?[^""\r\n]*""?[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex BaseDirLine();

    [GeneratedRegex(@"^(?<key>datadir=)""?[^""\r\n]*""?[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex DataDirLine();

    [GeneratedRegex(@"^(?<key>pid-file=)""?[^""\r\n]*""?[ \t]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PidFileLine();
}
