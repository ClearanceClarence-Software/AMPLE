using Ample.Core;
using Ample.Services;
using Ample.Sites;
using Microsoft.Win32;

namespace Ample.Stack;

/// <summary>
/// Installs and repairs a stack from a manifest: acquires each component, unpacks it into place, writes the configs,
/// creates the database and records everything in stack.json. Safe to run again: components already recorded are
/// not in the plan, and config files the user may have edited are only written when missing.
/// </summary>
/// <param name="host">AMPLE: paths, settings, stack state and the log.</param>
public sealed class StackInstaller(IServiceHost host)
{
    private const string Tag = "install";

    private readonly ComponentSource _source = new(host);

    private AmplePaths Paths => host.Paths;

    private StackState Stack => host.Stack;

    /// <summary>Runs a plan to the end, or to the first failure.</summary>
    /// <param name="plan">What to install.</param>
    /// <param name="progress">Receives steps, details and fractions for the setup screen.</param>
    /// <param name="ct">Cancels between steps and during downloads; a half-placed component is removed.</param>
    /// <returns>Success once every component is placed and the stack is configured.</returns>
    public async Task<InstallResult> InstallAsync(InstallPlan plan, InstallProgress progress, CancellationToken ct)
    {
        try
        {
            Paths.EnsureFolders();
            foreach (ManifestComponent component in plan.Components)
            {
                ct.ThrowIfCancellationRequested();
                progress.Step($"Getting {component.Name} {component.Version}");
                progress.Fraction(null);
                AcquiredFile file = await _source.AcquireAsync(component, progress, ct);

                progress.Step($"Installing {component.Name} {component.Version}");
                progress.Detail("");
                progress.Fraction(null);
                await PlaceAsync(component, file.Path, ct);

                Stack.Components[component.StackId] = new InstalledComponent
                {
                    Version = component.Version,
                    Sha256 = file.Sha256,
                    Source = file.Source,
                    InstalledAt = DateTimeOffset.Now,
                };
                Stack.ManifestUpdated = plan.Manifest.Updated;
                Stack.ManifestChannel = plan.Manifest.Channel;
                Stack.Save(Paths.StackFile);
                host.Write(Tag, $"{component.Name} {component.Version} installed.", LogLevel.Ok);
            }

            if (plan.DefaultPhp is { } defaultPhp && Paths.InstalledPhpVersions().Contains(defaultPhp))
            {
                Stack.DefaultPhp = defaultPhp;
            }

            progress.Step("Writing the configuration");
            progress.Detail("");
            InstallResult configured = await ConfigureAsync(progress, ct);
            Stack.Save(Paths.StackFile);
            return configured;
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Setup was canceled. Run it again to continue where it stopped.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                       or InvalidDataException)
        {
            host.Write(Tag, ex.Message, LogLevel.Error);
            return new InstallResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Writes every config the stack needs and is missing, initializes the database the first time, configures the
    /// database tools and issues the localhost certificate. Also used on its own as "Repair".
    /// </summary>
    /// <param name="progress">Receives the step names.</param>
    /// <param name="ct">Cancels between steps.</param>
    /// <returns>Success when the stack can start.</returns>
    public async Task<InstallResult> ConfigureAsync(InstallProgress progress, CancellationToken ct)
    {
        Paths.EnsureFolders();
        List<string> phpVersions = Paths.InstalledPhpVersions();
        if (phpVersions.Count == 0)
        {
            return new InstallResult(false, @"No PHP version is installed under bin\php.");
        }

        if (Stack.DefaultPhp is null || !phpVersions.Contains(Stack.DefaultPhp))
        {
            Stack.DefaultPhp = phpVersions[0];
        }
        string defaultPhp = Stack.DefaultPhp;

        foreach (string version in phpVersions)
        {
            if (PhpSetup.WriteIniIfMissing(Paths, version))
            {
                host.Write(Tag, $"Wrote php.ini for PHP {version}.");
            }
        }

        if (ApacheConfig.WriteHttpdConfIfMissing(Paths))
        {
            host.Write(Tag, @"Wrote etc\apache\httpd.conf.");
        }
        WriteGeneratedApacheFiles(defaultPhp);
        WriteLocalhostPages();

        List<string> mariaDbVersions = Paths.InstalledMariaDbVersions();
        if (mariaDbVersions.Count == 0)
        {
            return new InstallResult(false, @"No MariaDB version is installed under bin\mariadb.");
        }
        if (Stack.ActiveMariaDb is null || !mariaDbVersions.Contains(Stack.ActiveMariaDb))
        {
            Stack.ActiveMariaDb = mariaDbVersions[0];
        }
        string activeMariaDb = Stack.ActiveMariaDb;

        if (MariaDbSetup.WriteIniIfMissing(Paths, activeMariaDb, host.Settings.Ports))
        {
            host.Write(Tag, @"Wrote etc\mariadb\my.ini.");
        }
        MariaDbSetup.SetActiveVersion(Paths, activeMariaDb);

        ct.ThrowIfCancellationRequested();
        if (!MariaDbSetup.IsInitialized(Paths, activeMariaDb))
        {
            progress.Step($"Creating the database (MariaDB {activeMariaDb})");
            Stack.MariaDbRootPassword ??= MariaDbSetup.NewPassword();
            Shell.RunResult result = await MariaDbSetup.InitializeAsync(
                Paths,
                activeMariaDb,
                Stack.MariaDbRootPassword,
                host.Settings.Ports.MariaDb);
            if (result.ExitCode != 0)
            {
                host.Write(Tag, "Creating the database failed:", LogLevel.Error);
                foreach (string line in (result.Error + "\n" + result.Output)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .TakeLast(8))
                {
                    host.Write(Tag, "  › " + line, LogLevel.Error);
                }
                return new InstallResult(false, "The database could not be created. The log shows why.");
            }
            host.Write(Tag, "Database created with a generated root password.", LogLevel.Ok);
        }

        if (Stack.Has("ledger") && Stack.MariaDbRootPassword is { } ledgerPassword)
        {
            progress.Step("Configuring Ledger");
            string? problem = await AppsSetup.ConfigureLedgerAsync(
                Paths,
                defaultPhp,
                ledgerPassword,
                host.Settings.Ports.MariaDb,
                host.Settings.DatabaseToolAutoLogin);
            if (problem is not null)
            {
                host.Write(Tag, problem, LogLevel.Warn);
            }
        }

        if (Stack.Has("phpmyadmin") && Stack.MariaDbRootPassword is { } phpMyAdminPassword)
        {
            AppsSetup.ConfigurePhpMyAdmin(
                Paths,
                phpMyAdminPassword,
                host.Settings.Ports.MariaDb,
                host.Settings.DatabaseToolAutoLogin);
        }

        progress.Step("Creating the HTTPS certificate");
        if (!LocalCertificates.EnsureServerCertificate(
            Paths,
            host.Settings.Projects.Where(project => project.UseHttps).Select(project => project.Host).ToList(),
            out string? certificateProblem))
        {
            host.Write(Tag, "The HTTPS certificate could not be created: " + certificateProblem, LogLevel.Warn);
        }

        Stack.RenderedRoot = Paths.Root;
        Stack.InstalledAt ??= DateTimeOffset.Now;
        return new InstallResult(true, "AMPLE is ready.");
    }

    /// <summary>Rewrites the generated Apache files for the current ports, PHP versions, apps and projects.</summary>
    /// <param name="defaultPhp">The default PHP version folder.</param>
    public void WriteGeneratedApacheFiles(string defaultPhp)
    {
        PortSettings ports = host.Settings.Ports;
        ApacheConfig.WritePorts(Paths, ports);
        ApacheConfig.WritePhp(Paths, defaultPhp, Paths.InstalledPhpVersions());
        ApacheConfig.WriteApps(Paths, defaultPhp, Directory.Exists(Paths.Ledger), Directory.Exists(Paths.PhpMyAdmin));
        ApacheConfig.WriteSsl(Paths, ports);
        ApacheConfig.WriteProjects(Paths, host.Settings.Projects, ports, defaultPhp);
    }

    /// <summary>
    /// When the stack folder was moved since the configs were written, points my.ini and every php.ini at the new
    /// root. Apache needs nothing: its root arrives on the command line.
    /// </summary>
    /// <returns>True when a move was detected and the files were updated.</returns>
    public bool RelocateIfMoved()
    {
        string? renderedRoot = Stack.RenderedRoot;
        if (renderedRoot is null || renderedRoot.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        host.Write(Tag, $"The AMPLE folder moved from {renderedRoot} to {Paths.Root}; updating the config files.");
        MariaDbSetup.Relocate(Paths, renderedRoot);
        foreach (string version in Paths.InstalledPhpVersions())
        {
            PhpSetup.Relocate(Paths, version);
        }
        if (Stack.DefaultPhp is { } defaultPhp)
        {
            WriteGeneratedApacheFiles(defaultPhp);
        }
        Stack.RenderedRoot = Paths.Root;
        Stack.Save(Paths.StackFile);
        return true;
    }

    /// <summary>Unpacks or runs one acquired component.</summary>
    /// <param name="component">The component.</param>
    /// <param name="file">Its verified archive or installer.</param>
    /// <param name="ct">Cancels before the work starts; unpacking itself runs to the end.</param>
    /// <exception cref="IOException">The archive is damaged or a folder cannot be replaced.</exception>
    private async Task PlaceAsync(ManifestComponent component, string file, CancellationToken ct)
    {
        if (component.Id.Equals("vcredist", StringComparison.OrdinalIgnoreCase))
        {
            await InstallVcRedistAsync(file);
            return;
        }

        await Task.Run(() => PlaceFiles(component, file), ct);
    }

    /// <summary>Unpacks one archive into its place in the stack.</summary>
    /// <param name="component">The component; its id decides the target folder.</param>
    /// <param name="file">Its verified archive.</param>
    /// <exception cref="IOException">The archive is damaged or a folder cannot be replaced.</exception>
    private void PlaceFiles(ManifestComponent component, string file)
    {
        switch (component.Id.ToLowerInvariant())
        {
            case "apache":
                ReplaceFolderFromZip(file, Paths.Apache, MarkerOf("apache"));
                Directory.CreateDirectory(Path.Combine(Paths.Apache, "modules"));
                break;
            case "mod_fcgid":
                PlaceFcgid(file);
                break;
            case "php":
                ReplaceFolderFromZip(file, Paths.Php(component.Version), MarkerOf("php"));
                break;
            case "mariadb":
                ReplaceFolderFromZip(file, Paths.MariaDb(component.Version), MarkerOf("mariadb"));
                break;
            case "ledger":
                ReplaceFolderFromZip(file, Paths.Ledger, MarkerOf("ledger"), keep: ["config.php", "logs"]);
                break;
            case "phpmyadmin":
                ReplaceFolderFromZip(file, Paths.PhpMyAdmin, MarkerOf("phpmyadmin"), keep: ["config.inc.php"]);
                break;
            default:
                throw new IOException(
                    $"The manifest names a component AMPLE doesn't know how to install: \"{component.Id}\".");
        }
    }

    /// <summary>
    /// The file that proves a component is in place, relative to its folder. Also how the right folder is found
    /// inside a zip, whatever the publisher wrapped it in.
    /// </summary>
    /// <param name="id">The component id.</param>
    /// <returns>A relative path such as bin\httpd.exe.</returns>
    public static string MarkerOf(string id) => id.ToLowerInvariant() switch
    {
        "apache" => Path.Combine("bin", "httpd.exe"),
        "mod_fcgid" => Path.Combine("modules", "mod_fcgid.so"),
        "php" => "php-cgi.exe",
        "mariadb" => Path.Combine("bin", "mariadbd.exe"),
        "ledger" => "index.php",
        "phpmyadmin" => "index.php",
        _ => "",
    };

    /// <summary>Whether a component's files are where they belong, by its marker file.</summary>
    /// <param name="paths">The stack.</param>
    /// <param name="component">The component.</param>
    /// <returns>True when the marker exists; true for components without one (the runtime installer).</returns>
    public static bool IsPlaced(AmplePaths paths, ManifestComponent component)
    {
        string marker = MarkerOf(component.Id);
        if (marker.Length == 0)
        {
            return true;
        }

        string folder = component.Id.ToLowerInvariant() switch
        {
            "apache" or "mod_fcgid" => paths.Apache,
            "php" => paths.Php(component.Version),
            "mariadb" => paths.MariaDb(component.Version),
            "ledger" => paths.Ledger,
            "phpmyadmin" => paths.PhpMyAdmin,
            _ => paths.Root,
        };
        return File.Exists(Path.Combine(folder, marker));
    }

    /// <summary>
    /// Unpacks a zip over a target folder: the old folder is removed (except the files to keep) and the zip's content
    /// folder moved into place.
    /// </summary>
    /// <param name="zip">Full path of the archive.</param>
    /// <param name="target">The folder to end up with the files.</param>
    /// <param name="marker">
    /// A file, relative to the component's folder, that marks the right folder inside the zip (Apache Lounge wraps
    /// Apache in Apache24\ next to two readme files, GitHub source zips in Ledger-&lt;tag&gt;\, and so on).
    /// </param>
    /// <param name="keep">Top-level names in the old folder carried over when the zip lacks them.</param>
    private void ReplaceFolderFromZip(string zip, string target, string marker, string[]? keep = null)
    {
        string staging = Path.Combine(Paths.Tmp, "staging", Path.GetFileName(target));
        string content;
        try
        {
            content = FileTools.ExtractToStaging(zip, staging);
        }
        catch (InvalidDataException ex)
        {
            throw new IOException($"{Path.GetFileName(zip)} is not a valid zip file: {ex.Message}", ex);
        }

        content = FolderHolding(content, marker)
            ?? throw new IOException($"{Path.GetFileName(zip)} holds no {marker}; is it the right archive?");

        if (Directory.Exists(target))
        {
            foreach (string name in keep ?? [])
            {
                string old = Path.Combine(target, name);
                string fresh = Path.Combine(content, name);
                if (File.Exists(old) && !File.Exists(fresh))
                {
                    File.Copy(old, fresh);
                }
                else if (Directory.Exists(old) && !Directory.Exists(fresh))
                {
                    FileTools.CopyDirectory(old, fresh);
                }
            }
            FileTools.ClearReadOnly(target);
            Directory.Delete(target, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(content, target);
        FileTools.TryDelete(staging);
    }

    /// <summary>The folder, at most two levels down, that holds the marker file.</summary>
    /// <param name="start">The extracted archive.</param>
    /// <param name="marker">The marker file's path relative to the folder wanted.</param>
    /// <returns>The folder; null when no folder within two levels holds the marker.</returns>
    private static string? FolderHolding(string start, string marker)
    {
        if (File.Exists(Path.Combine(start, marker)))
        {
            return start;
        }

        foreach (string child in Directory.EnumerateDirectories(start))
        {
            if (File.Exists(Path.Combine(child, marker)))
            {
                return child;
            }
            foreach (string grandchild in Directory.EnumerateDirectories(child))
            {
                if (File.Exists(Path.Combine(grandchild, marker)))
                {
                    return grandchild;
                }
            }
        }
        return null;
    }

    /// <summary>Copies mod_fcgid.so from the Apache Lounge zip into Apache's modules folder.</summary>
    /// <param name="zip">Full path of the archive.</param>
    private void PlaceFcgid(string zip)
    {
        string staging = Path.Combine(Paths.Tmp, "staging", "mod_fcgid");
        FileTools.ExtractToStaging(zip, staging);
        string? module = Directory
            .EnumerateFiles(staging, "mod_fcgid.so", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (module is null)
        {
            throw new IOException("The mod_fcgid archive holds no mod_fcgid.so.");
        }

        string modules = Path.Combine(Paths.Apache, "modules");
        Directory.CreateDirectory(modules);
        File.Copy(module, Path.Combine(modules, "mod_fcgid.so"), overwrite: true);
        FileTools.TryDelete(staging);
    }

    /// <summary>Runs the Visual C++ runtime installer quietly when the runtime isn't there yet.</summary>
    /// <param name="installer">Full path of vc_redist.x64.exe.</param>
    private async Task InstallVcRedistAsync(string installer)
    {
        if (IsVcRuntimeInstalled())
        {
            host.Debug(Tag, "The Visual C++ runtime is already installed.");
            return;
        }

        host.Write(Tag, "Installing the Visual C++ runtime (Windows asks for admin rights)…");
        int exitCode = await Shell.RunElevatedAsync(installer, "/install /quiet /norestart", Paths.Tmp);
        // 0: installed; 1638: a newer runtime is there; 3010: installed, reboot pending.
        if (exitCode is not (0 or 1638 or 3010))
        {
            string reason = exitCode == -2 ? "the admin prompt was declined" : $"exit code {exitCode}";
            host.Write(
                Tag,
                $"The Visual C++ runtime did not install ({reason}). Apache and PHP need it to start.",
                LogLevel.Warn);
        }
    }

    /// <summary>Whether the Visual C++ 2015-2022 x64 runtime is registered.</summary>
    /// <returns>True when the registry says it is installed; false off Windows or when it isn't.</returns>
    public static bool IsVcRuntimeInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
        return key?.GetValue("Installed") is int installed && installed == 1;
    }

    /// <summary>
    /// Writes the pages AMPLE serves on localhost: the welcome page (once, so edits survive), and under www\_ample
    /// the phpinfo page per PHP version plus stack.json, which the welcome page reads. The _ample folder is AMPLE's
    /// and is rewritten every time.
    /// </summary>
    public void WriteLocalhostPages()
    {
        string welcome = Path.Combine(Paths.Www, "index.php");
        if (!File.Exists(welcome) && !File.Exists(Path.Combine(Paths.Www, "index.html")))
        {
            ConfigTemplates.WriteFile(welcome, ConfigTemplates.Read("www/index.php"));
        }

        string phpInfoPage = ConfigTemplates.Read("www/phpinfo.php");
        foreach (string version in Paths.InstalledPhpVersions())
        {
            ConfigTemplates.WriteFile(Path.Combine(Paths.WwwAmple, "php", version, "index.php"), phpInfoPage);
        }
        ConfigTemplates.WriteFile(Path.Combine(Paths.WwwAmple, "stack.json"), LocalhostStackJson());
    }

    /// <summary>What the welcome page shows: versions, tools, ports and projects, as JSON.</summary>
    /// <returns>Indented JSON.</returns>
    private string LocalhostStackJson()
    {
        PortSettings ports = host.Settings.Ports;
        var document = new
        {
            ample = typeof(StackInstaller).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            root = Paths.Root,
            ports = new { http = ports.Http, https = ports.Https, mariadb = ports.MariaDb },
            php = new
            {
                @default = Stack.DefaultPhp,
                installed = Paths.InstalledPhpVersions(),
            },
            mariadb = new
            {
                active = Stack.ActiveMariaDb,
                installed = Paths.InstalledMariaDbVersions(),
            },
            tools = new
            {
                ledger = Directory.Exists(Paths.Ledger),
                phpmyadmin = Directory.Exists(Paths.PhpMyAdmin),
                preferred = host.Settings.DatabaseTool == DatabaseTool.Ledger ? "ledger" : "phpmyadmin",
            },
            projects = host.Settings.Projects.Select(project => new
            {
                name = project.Name,
                host = project.Host,
                folder = project.Folder,
                https = project.UseHttps,
                php = project.PhpVersion ?? Stack.DefaultPhp,
            }),
        };
        return System.Text.Json.JsonSerializer.Serialize(
            document,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}
