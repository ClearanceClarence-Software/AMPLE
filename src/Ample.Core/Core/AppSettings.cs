using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ample.Core;

// Property names in this file are the settings.json keys: renaming one resets that setting for every user.

/// <summary>The database tool the Overview opens for the databases.</summary>
public enum DatabaseTool
{
    /// <summary>Ledger, which ships with AMPLE.</summary>
    Ledger,

    /// <summary>phpMyAdmin, installed on request.</summary>
    PhpMyAdmin,
}

/// <summary>The ports AMPLE's servers listen on, written into their configs and checked for conflicts.</summary>
public sealed class PortSettings
{
    /// <summary>Apache's HTTP port.</summary>
    public int Http { get; set; } = 80;

    /// <summary>Apache's HTTPS port.</summary>
    public int Https { get; set; } = 443;

    /// <summary>MariaDB's TCP port.</summary>
    public int MariaDb { get; set; } = 3306;

    /// <summary>Every port, for the conflict check.</summary>
    public IReadOnlyList<int> All => [Http, Https, MariaDb];
}

/// <summary>A local project served by Apache under its own host name, with its own PHP version.</summary>
public sealed class Project
{
    /// <summary>The name shown in the list, e.g. "Flix".</summary>
    public string Name { get; set; } = "";

    /// <summary>The host name, such as flix.test; lowercase, without scheme or port.</summary>
    public string Host { get; set; } = "";

    /// <summary>Full path of the project's document root.</summary>
    public string Folder { get; set; } = "";

    /// <summary>The PHP version folder under bin\php this project runs on; null uses the default version.</summary>
    public string? PhpVersion { get; set; }

    /// <summary>Full path of a php.ini just for this project; null uses the version's own php.ini.</summary>
    public string? PhpIni { get; set; }

    /// <summary>True also serves the project over HTTPS with AMPLE's local certificate.</summary>
    public bool UseHttps { get; set; } = true;
}

/// <summary>User settings, stored as JSON in %AppData%\AMPLE\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>The AMPLE folder to manage; null manages the folder AMPLE.exe runs from.</summary>
    public string? Root { get; set; }

    /// <summary>Editor for config and log files, as a full path or a name on PATH; null uses Notepad.</summary>
    public string? Editor { get; set; }

    /// <summary>Browser for the Open links; empty uses the system default.</summary>
    public string? Browser { get; set; }

    /// <summary>The tool the Open button launches for the databases.</summary>
    public DatabaseTool DatabaseTool { get; set; } = DatabaseTool.Ledger;

    /// <summary>True signs the database tool in with the local root account instead of asking each time.</summary>
    public bool DatabaseToolAutoLogin { get; set; } = true;

    /// <summary>True starts every service when AMPLE opens.</summary>
    public bool StartServicesOnLaunch { get; set; } = true;

    /// <summary>True registers AMPLE to launch when the user signs in to Windows.</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>True keeps AMPLE running in the notification area when the window is closed.</summary>
    public bool KeepInTray { get; set; } = true;

    /// <summary>True opens the main window minimized.</summary>
    public bool StartMinimized { get; set; }

    /// <summary>The color theme: System, Light or Dark.</summary>
    public string Theme { get; set; } = AppTheme.System;

    /// <summary>True checks GitHub for a newer AMPLE and a newer manifest when it starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>True shows debug lines in the log.</summary>
    public bool ShowDebug { get; set; }

    /// <summary>The ports the servers listen on.</summary>
    public PortSettings Ports { get; set; } = new();

    /// <summary>The projects, in the order they are written to the Apache config.</summary>
    public List<Project> Projects { get; set; } = [];

    /// <summary>Lines the log keeps before dropping the oldest, 100 to 100,000 after <see cref="Load"/>.</summary>
    public int MaxLogLines { get; set; } = 2000;

    /// <summary>True also appends log lines to <see cref="LogFilePath"/>.</summary>
    public bool LogToFile { get; set; }

    /// <summary>The settings folder, %AppData%\AMPLE; it may not exist yet.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AMPLE");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>The log file written when <see cref="LogToFile"/> is on.</summary>
    public static string LogFilePath => Path.Combine(Folder, "ample.log");

    /// <summary>Reads settings.json and clamps values into range.</summary>
    /// <returns>The stored settings; defaults when the file is missing, unreadable or corrupt.</returns>
    public static AppSettings Load()
    {
        AppSettings settings = ReadFile();
        settings.Ports ??= new PortSettings();
        settings.Projects ??= [];
        settings.Theme = AppTheme.Normalize(settings.Theme);
        settings.MaxLogLines = Math.Clamp(settings.MaxLogLines, 100, 100_000);
        return settings;
    }

    /// <summary>Deserializes settings.json.</summary>
    /// <returns>The stored settings; defaults when the file is missing, unreadable, corrupt or the JSON null.</returns>
    private static AppSettings ReadFile()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings)
                ?? new AppSettings();
        }
        catch
        {
            // Settings must never stop AMPLE from starting: a corrupt or unreadable file means defaults.
            return new AppSettings();
        }
    }

    /// <summary>Writes these settings to settings.json, creating the folder when needed.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
    }

    /// <summary>Deep copy, so dialogs can edit without touching the live settings until Save.</summary>
    /// <returns>A new instance with no references shared with this one.</returns>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize(
            JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings),
            SettingsJson.Default.AppSettings)
        ?? throw new InvalidOperationException("Serializing the settings produced the JSON literal null.");

    /// <summary>Copies every user-facing setting from an edited copy into this instance.</summary>
    /// <param name="other">Usually a <see cref="Clone"/> a page edited; its lists are copied.</param>
    public void CopyFrom(AppSettings other)
    {
        Root = other.Root;
        Editor = other.Editor;
        Browser = other.Browser;
        DatabaseTool = other.DatabaseTool;
        DatabaseToolAutoLogin = other.DatabaseToolAutoLogin;
        StartServicesOnLaunch = other.StartServicesOnLaunch;
        LaunchAtStartup = other.LaunchAtStartup;
        KeepInTray = other.KeepInTray;
        StartMinimized = other.StartMinimized;
        Theme = other.Theme;
        CheckForUpdates = other.CheckForUpdates;
        ShowDebug = other.ShowDebug;
        Ports = new PortSettings { Http = other.Ports.Http, Https = other.Ports.Https, MariaDb = other.Ports.MariaDb };
        Projects = [.. other.Projects];
        MaxLogLines = other.MaxLogLines;
        LogToFile = other.LogToFile;
    }
}

/// <summary>Source-generated JSON metadata for settings.json: indented, enums as text, nulls left out.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
