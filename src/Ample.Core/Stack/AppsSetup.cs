using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Ample.Core;

namespace Ample.Stack;

/// <summary>Preconfigures the database tools so they open signed in, without their own installers.</summary>
public static partial class AppsSetup
{
    /// <summary>The admin user AMPLE creates in Ledger; the password is the MariaDB root password.</summary>
    public const string LedgerAdminUser = "admin";

    /// <summary>Full path of Ledger's config.php.</summary>
    /// <param name="paths">The stack.</param>
    /// <returns>apps\ledger\config.php; the file may not exist.</returns>
    public static string LedgerConfigPath(AmplePaths paths) => Path.Combine(paths.Ledger, "config.php");

    /// <summary>Full path of phpMyAdmin's config.inc.php.</summary>
    /// <param name="paths">The stack.</param>
    /// <returns>apps\phpmyadmin\config.inc.php; the file may not exist.</returns>
    public static string PhpMyAdminConfigPath(AmplePaths paths) => Path.Combine(paths.PhpMyAdmin, "config.inc.php");

    /// <summary>
    /// Writes Ledger's config.php from its own config.template.php, with the database connection and an admin user,
    /// unless Ledger is already configured.
    /// </summary>
    /// <param name="paths">The stack.</param>
    /// <param name="phpVersion">A PHP version to hash the admin password with (bcrypt, as Ledger's installer
    /// does).</param>
    /// <param name="rootPassword">The MariaDB root password; also the Ledger admin password.</param>
    /// <param name="port">MariaDB's port.</param>
    /// <param name="autoLogin">True opens Ledger without its login form.</param>
    /// <returns>Null when written or already present; otherwise why it could not be written.</returns>
    public static async Task<string?> ConfigureLedgerAsync(
        AmplePaths paths,
        string phpVersion,
        string rootPassword,
        int port,
        bool autoLogin)
    {
        string configPath = LedgerConfigPath(paths);
        if (File.Exists(configPath))
        {
            SetLedgerAutoLogin(paths, autoLogin);
            return null;
        }

        string templatePath = Path.Combine(paths.Ledger, "config.template.php");
        if (!File.Exists(templatePath))
        {
            return "Ledger has no config.template.php; its files may not have unpacked.";
        }

        string? hash = await BcryptAsync(paths, phpVersion, rootPassword);
        if (hash is null)
        {
            return "PHP could not hash the Ledger admin password.";
        }

        string config = File.ReadAllText(templatePath)
            .Replace("{{DB_HOST}}", "127.0.0.1", StringComparison.Ordinal)
            .Replace("{{DB_PORT}}", port.ToString(), StringComparison.Ordinal)
            .Replace("{{DB_USER}}", "root", StringComparison.Ordinal)
            .Replace("{{DB_PASS}}", PhpEscape(rootPassword), StringComparison.Ordinal)
            .Replace("{{ADMIN_USER}}", LedgerAdminUser, StringComparison.Ordinal)
            .Replace("{{ADMIN_HASH}}", hash, StringComparison.Ordinal)
            .Replace(
                "{{HIDDEN_DBS}}",
                "['information_schema', 'performance_schema', 'mysql', 'sys', 'phpmyadmin']",
                StringComparison.Ordinal)
            .Replace("{{INSTALL_DATE}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal);

        Directory.CreateDirectory(Path.Combine(paths.Ledger, "logs"));
        ConfigTemplates.WriteFile(configPath, config);
        SetLedgerAutoLogin(paths, autoLogin);
        return null;
    }

    /// <summary>
    /// Turns Ledger's login form off or on by flipping 'require_auth' in its config.php. Off, Ledger opens straight
    /// away; the admin user stays in the file for when it is turned back on.
    /// </summary>
    /// <param name="paths">The stack.</param>
    /// <param name="autoLogin">True opens Ledger without a login.</param>
    /// <returns>True when config.php was changed; false when it is missing or already matched.</returns>
    public static bool SetLedgerAutoLogin(AmplePaths paths, bool autoLogin)
    {
        string configPath = LedgerConfigPath(paths);
        if (!File.Exists(configPath))
        {
            return false;
        }

        string text = FileTools.ReadText(configPath, out var encoding);
        string updated = RequireAuthLine().Replace(text, $"${{key}}{(autoLogin ? "false" : "true")}", 1);
        if (updated == text)
        {
            return false;
        }

        File.WriteAllText(configPath, updated, encoding);
        return true;
    }

    /// <summary>
    /// Switches phpMyAdmin between signing in as root automatically and showing its login form, in an existing
    /// config.inc.php.
    /// </summary>
    /// <param name="paths">The stack.</param>
    /// <param name="rootPassword">The MariaDB root password, written only when auto login is on.</param>
    /// <param name="autoLogin">True signs in automatically.</param>
    /// <returns>True when config.inc.php was changed; false when it is missing or already matched.</returns>
    public static bool SetPhpMyAdminAutoLogin(AmplePaths paths, string rootPassword, bool autoLogin)
    {
        string configPath = PhpMyAdminConfigPath(paths);
        if (!File.Exists(configPath))
        {
            return false;
        }

        string text = FileTools.ReadText(configPath, out var encoding);
        string updated = AuthTypeLine().Replace(text, $"${{key}}'{(autoLogin ? "config" : "cookie")}';", 1);
        updated = PasswordLine().Replace(updated, $"${{key}}'{(autoLogin ? PhpEscape(rootPassword) : "")}';", 1);
        if (updated == text)
        {
            return false;
        }

        File.WriteAllText(configPath, updated, encoding);
        return true;
    }

    /// <summary>Writes phpMyAdmin's config.inc.php from AMPLE's template unless one exists.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="rootPassword">The MariaDB root password.</param>
    /// <param name="port">MariaDB's port.</param>
    /// <param name="autoLogin">True signs in as root automatically; false shows phpMyAdmin's login form.</param>
    /// <returns>True when written; false when a config already existed.</returns>
    public static bool ConfigurePhpMyAdmin(AmplePaths paths, string rootPassword, int port, bool autoLogin)
    {
        string configPath = PhpMyAdminConfigPath(paths);
        if (File.Exists(configPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.Combine(paths.Tmp, "phpmyadmin"));
        string text = ConfigTemplates.Render("phpmyadmin/config.inc.php", new Dictionary<string, string>
        {
            ["AMPLE_ROOT"] = paths.RootForConfig,
            ["MARIADB_PORT"] = port.ToString(),
            ["AUTH_TYPE"] = autoLogin ? "config" : "cookie",
            ["ROOT_PASSWORD"] = autoLogin ? PhpEscape(rootPassword) : "",
            ["BLOWFISH_SECRET"] = RandomNumberGenerator.GetHexString(32, lowercase: true),
        });
        ConfigTemplates.WriteFile(configPath, text);
        return true;
    }

    /// <summary>Hashes a password with bcrypt by asking PHP, exactly as Ledger's own installer would.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="phpVersion">The PHP version whose php.exe runs.</param>
    /// <param name="password">The password; handed over in the environment, never on the command line.</param>
    /// <returns>The "$2y$…" hash; null when php.exe is missing or fails.</returns>
    private static async Task<string?> BcryptAsync(AmplePaths paths, string phpVersion, string password)
    {
        string phpExe = Path.Combine(paths.Php(phpVersion), "php.exe");
        if (!File.Exists(phpExe))
        {
            return null;
        }

        const string script = "echo password_hash(getenv('AMPLE_PASSWORD'), PASSWORD_BCRYPT);";
        var environment = new Dictionary<string, string> { ["AMPLE_PASSWORD"] = password };
        Shell.RunResult result = await Shell.RunAsync(
            phpExe,
            $"-n -r \"{script}\"",
            paths.Root,
            TimeSpan.FromSeconds(30),
            environment);
        string hash = result.Output.Trim();
        return result.ExitCode == 0 && hash.StartsWith("$2", StringComparison.Ordinal) ? hash : null;
    }

    [GeneratedRegex(@"(?<key>'require_auth'\s*=>\s*)(true|false)")]
    private static partial Regex RequireAuthLine();

    [GeneratedRegex(@"(?<key>\$cfg\['Servers'\]\[\$i\]\['auth_type'\]\s*=\s*)'[^']*';")]
    private static partial Regex AuthTypeLine();

    [GeneratedRegex(@"(?<key>\$cfg\['Servers'\]\[\$i\]\['password'\]\s*=\s*)'(?:[^'\\]|\\.)*';")]
    private static partial Regex PasswordLine();

    /// <summary>Escapes a value for a single-quoted PHP string.</summary>
    private static string PhpEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
