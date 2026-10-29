using Ample.Core;
using Ample.Php;

namespace Ample.Stack;

/// <summary>Turns a freshly unpacked PHP build into one that works in the stack: a php.ini with sane
/// settings.</summary>
public static class PhpSetup
{
    /// <summary>The extensions turned on in every version; those a build lacks are skipped.</summary>
    public static readonly string[] Extensions =
    [
        "bz2", "curl", "fileinfo", "gd", "gettext", "intl", "mbstring", "exif", "mysqli", "openssl", "pdo_mysql",
        "pdo_sqlite", "soap", "sodium", "sqlite3", "zip",
    ];

    /// <summary>Writes php.ini for one version from the build's php.ini-development, unless one already
    /// exists.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The folder name under bin\php.</param>
    /// <returns>True when php.ini was written; false when the version already had one.</returns>
    /// <exception cref="IOException">The build has no php.ini-development or the file cannot be written.</exception>
    public static bool WriteIniIfMissing(AmplePaths paths, string version)
    {
        string folder = paths.Php(version);
        string iniPath = Path.Combine(folder, "php.ini");
        if (File.Exists(iniPath))
        {
            return false;
        }

        string? source = AmplePaths.FirstExisting(
            Path.Combine(folder, "php.ini-development"),
            Path.Combine(folder, "php.ini-production"));
        if (source is null)
        {
            throw new IOException($"PHP {version} has no php.ini-development to start from.");
        }

        File.Copy(source, iniPath);
        ApplyStackSettings(paths, version, iniPath);
        return true;
    }

    /// <summary>Sets the directives that tie php.ini to the stack's folders, and turns the usual extensions
    /// on.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The folder name under bin\php.</param>
    /// <param name="iniPath">Full path of the php.ini to change.</param>
    public static void ApplyStackSettings(AmplePaths paths, string version, string iniPath)
    {
        string folder = paths.Php(version);
        string root = paths.RootForConfig;
        Directory.CreateDirectory(Path.Combine(paths.Tmp, "php-sessions"));
        Directory.CreateDirectory(Path.Combine(paths.Tmp, "php-uploads"));

        PhpIni ini = PhpIni.Load(iniPath);
        ini.SetValue("extension_dir", AmplePaths.ConfigPath(Path.Combine(folder, "ext")));
        ini.SetValue("error_log", $"{root}/logs/php/php_errors.log");
        ini.SetValue("log_errors", "On");
        ini.SetValue("display_errors", "On");
        ini.SetValue("error_reporting", "E_ALL");
        ini.SetValue("memory_limit", "512M");
        ini.SetValue("max_execution_time", "120");
        ini.SetValue("max_input_time", "120");
        ini.SetValue("post_max_size", "256M");
        ini.SetValue("upload_max_filesize", "256M");
        ini.SetValue("max_file_uploads", "50");
        ini.SetValue("session.save_path", $"{root}/tmp/php-sessions");
        ini.SetValue("upload_tmp_dir", $"{root}/tmp/php-uploads");
        ini.SetValue("sys_temp_dir", $"{root}/tmp");
        ini.SetValue("cgi.fix_pathinfo", "1");
        ini.SetValue("opcache.enable", "1");
        ini.SetValue("opcache.enable_cli", "0");

        var available = ini.ListExtensions(Path.Combine(folder, "ext"));
        foreach (PhpExtension extension in available)
        {
            bool wanted = Extensions.Contains(extension.Name, StringComparer.OrdinalIgnoreCase)
                || extension.Name.Equals("opcache", StringComparison.OrdinalIgnoreCase);
            if (wanted && !extension.IsEnabled)
            {
                ini.SetExtensionEnabled(extension, true);
            }
        }

        ini.Save();
    }

    /// <summary>Points an existing php.ini at a new root after the stack moved.</summary>
    /// <param name="paths">The stack at its new root.</param>
    /// <param name="version">The folder name under bin\php.</param>
    public static void Relocate(AmplePaths paths, string version)
    {
        string iniPath = Path.Combine(paths.Php(version), "php.ini");
        if (File.Exists(iniPath))
        {
            ApplyStackSettings(paths, version, iniPath);
        }
    }

    /// <summary>"PHP 8.4.14" from a version folder's php-cgi.exe, or the folder name when the exe has no
    /// version.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="version">The folder name under bin\php.</param>
    /// <returns>The label.</returns>
    public static string Label(AmplePaths paths, string version) =>
        "PHP " + (AmplePaths.FileVersion(Path.Combine(paths.Php(version), "php-cgi.exe")) ?? version);
}
