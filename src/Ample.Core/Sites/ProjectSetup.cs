using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Ample.Core;
using Ample.Services;
using Ample.Stack;

namespace Ample.Sites;

/// <summary>The outcome of applying the projects.</summary>
/// <param name="IsSuccess">True when Apache's config was written and passed its test.</param>
/// <param name="Message">One sentence for the status line; the details are in the log.</param>
public sealed record ProjectSetupResult(bool IsSuccess, string Message);

/// <summary>
/// Applies the Projects page's list: certificate, virtual hosts, a config test (undone when it fails), the hosts
/// file and an Apache restart.
/// </summary>
public static partial class ProjectSetup
{
    private const string Tag = "projects";

    // Top-level domains on the HSTS preload list: browsers refuse plain HTTP for every name under them.
    private static readonly string[] _httpsOnlyDomains = ["dev", "app", "page", "new", "day", "foo"];

    /// <summary>A host name suggestion for a project: its name lowercased and cleaned, plus ".test".</summary>
    /// <param name="name">The project's name or folder name.</param>
    /// <returns>e.g. "my-project.test"; "project.test" when nothing usable is left.</returns>
    public static string SuggestHost(string name)
    {
        string slug = NotHostCharacter().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return (slug.Length == 0 ? "project" : slug) + ".test";
    }

    /// <summary>Checks the projects before anything is written.</summary>
    /// <param name="projects">The projects as edited, host names already trimmed and lowercase.</param>
    /// <returns>The first problem as a sentence for the status line; null when every project is usable.</returns>
    public static string? FindProblem(IReadOnlyList<Project> projects)
    {
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in projects)
        {
            if (project.Name.Trim().Length == 0)
            {
                return "Every project needs a name.";
            }
            if (project.Host.Length == 0)
            {
                return $"{project.Name} needs a host name, such as {SuggestHost(project.Name)}.";
            }
            if (project.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return "localhost is already served from www; give the project its own name, such as myproject.test.";
            }
            if (!HostName().IsMatch(project.Host))
            {
                return $"\"{project.Host}\" isn't a valid host name. Use letters, digits and hyphens with a dot, " +
                    "such as myproject.test.";
            }
            if (!seenHosts.Add(project.Host))
            {
                return $"\"{project.Host}\" is used by two projects.";
            }
            if (project.Folder.Length == 0 || !Path.IsPathFullyQualified(project.Folder))
            {
                return $"Choose a full folder path for {project.Name}.";
            }

            string topLevelDomain = project.Host[(project.Host.LastIndexOf('.') + 1)..];
            if (!project.UseHttps && _httpsOnlyDomains.Contains(topLevelDomain, StringComparer.OrdinalIgnoreCase))
            {
                return $"Browsers only open .{topLevelDomain} names over HTTPS, "
                    + $"so {project.Host} needs HTTPS turned on.";
            }
        }
        return null;
    }

    /// <summary>Writes everything the projects need and restarts Apache when it runs.</summary>
    /// <param name="host">AMPLE: paths, settings, stack state and the log.</param>
    /// <param name="projects">The projects, already checked with <see cref="FindProblem"/>; empty removes
    /// them all.</param>
    /// <param name="restartApache">Restarts the web server when it is running; called after a successful write.</param>
    /// <param name="status">Receives a short progress line before each step.</param>
    /// <returns>
    /// Success once the config passed its test and the settings were saved, even when the hosts file or the trust
    /// prompt was declined (those are logged as warnings); a failure when nothing was kept.
    /// </returns>
    public static async Task<ProjectSetupResult> ApplyAsync(
        IServiceHost host,
        IReadOnlyList<Project> projects,
        Func<Task> restartApache,
        Action<string> status)
    {
        AmplePaths paths = host.Paths;
        if (!File.Exists(paths.HttpdConf))
        {
            return new ProjectSetupResult(false, @"No Apache configuration in etc\apache. Run Repair from Settings.");
        }

        string defaultPhp = host.Stack.DefaultPhp ?? paths.InstalledPhpVersions().FirstOrDefault() ?? "";
        if (defaultPhp.Length == 0)
        {
            return new ProjectSetupResult(false, "No PHP version is installed, so the projects can't be served.");
        }

        CreateMissingFolders(host, projects);

        // Everything Apply may change, so a failed config test leaves Apache as it was.
        FileSnapshot snapshot = await FileSnapshot.TakeAsync(
        [
            paths.ProjectsConf,
            LocalCertificates.ServerCertificatePath(paths),
            LocalCertificates.ServerKeyPath(paths),
        ]);

        List<string> httpsHosts = projects.Where(project => project.UseHttps).Select(project => project.Host).ToList();
        status("Creating the HTTPS certificate…");
        if (await IssueCertificateAsync(host, httpsHosts) is { } certificateFailure)
        {
            await snapshot.RestoreAsync();
            return new ProjectSetupResult(false, certificateFailure);
        }

        status("Writing the virtual hosts…");
        if (await WriteAndTestConfigAsync(host, projects, defaultPhp) is { } failure)
        {
            await snapshot.RestoreAsync();
            return new ProjectSetupResult(false, failure);
        }

        host.Settings.Projects = [.. projects];
        host.Settings.Save();

        status("Updating the hosts file (Windows asks for admin rights)…");
        await UpdateHostsAsync(host, projects);

        if (httpsHosts.Count > 0)
        {
            await TrustAuthorityAsync(host);
        }

        status("Restarting the web server…");
        await restartApache();

        string summary = projects.Count == 0
            ? "The projects were removed."
            : $"{projects.Count} project{(projects.Count == 1 ? "" : "s")} set up.";
        host.Write(Tag, summary, LogLevel.Ok);
        return new ProjectSetupResult(true, summary);
    }

    /// <summary>Creates the document roots that don't exist yet, so Apache doesn't refuse to start.</summary>
    private static void CreateMissingFolders(IServiceHost host, IReadOnlyList<Project> projects)
    {
        foreach (Project project in projects.Where(project => !Directory.Exists(project.Folder)))
        {
            Directory.CreateDirectory(project.Folder);
            host.Write(Tag, $"Created the folder \"{project.Folder}\" for {project.Host}.");
        }
    }

    /// <summary>Issues the server certificate for localhost and the HTTPS projects.</summary>
    /// <returns>Null when the certificate was written; otherwise the reason for the status line.</returns>
    private static async Task<string?> IssueCertificateAsync(IServiceHost host, List<string> httpsHosts)
    {
        string? problem = null;
        await Task.Run(() => LocalCertificates.EnsureServerCertificate(host.Paths, httpsHosts, out problem));
        if (problem is not null)
        {
            host.Write(Tag, "Creating the certificate failed: " + problem, LogLevel.Error);
            return "The HTTPS certificate couldn't be created, so nothing was changed. The log shows why.";
        }

        string names = httpsHosts.Count == 0 ? "localhost" : "localhost and " + string.Join(", ", httpsHosts);
        host.Write(Tag, $"Certificate issued for {names}.");
        return null;
    }

    /// <summary>Writes projects.conf, then tests the config with httpd -t.</summary>
    /// <returns>Null when the config passed; otherwise the reason, and the caller restores the files.</returns>
    private static async Task<string?> WriteAndTestConfigAsync(
        IServiceHost host,
        IReadOnlyList<Project> projects,
        string defaultPhp)
    {
        try
        {
            ApacheConfig.WriteProjects(host.Paths, projects, host.Settings.Ports, defaultPhp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Write(Tag, "Writing the Apache configuration failed: " + ex.Message, LogLevel.Error);
            return "The Apache configuration couldn't be written, so nothing was changed.";
        }

        Shell.RunResult test = await ApacheService.TestConfigAsync(host.Paths);
        if (test.TimedOut || test.ExitCode == 0)
        {
            return null;
        }

        host.Write(Tag, "Apache rejected the new configuration, so it was undone:", LogLevel.Error);
        foreach (string line in ApacheService.ConfigTestProblems(test))
        {
            host.Write(Tag, "  › " + line, LogLevel.Error);
        }
        return "Apache rejected the configuration, so it was undone. The log shows why.";
    }

    /// <summary>Points the host names at this computer and logs what happened.</summary>
    private static async Task UpdateHostsAsync(IServiceHost host, IReadOnlyList<Project> projects)
    {
        int exitCode;
        try
        {
            exitCode = await HostsFile.UpdateAsync(projects.Select(project => project.Host).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Write(Tag, "The hosts file couldn't be read or prepared: " + ex.Message, LogLevel.Warn);
            return;
        }

        if (exitCode == -2)
        {
            host.Write(Tag,
                "The hosts file wasn't changed (canceled at the admin prompt), so the new names don't resolve yet. " +
                "Apply again to retry.",
                LogLevel.Warn);
        }
        else if (exitCode != 0)
        {
            host.Write(Tag,
                $"Writing the hosts file failed (exit code {exitCode}). An antivirus may be protecting it.",
                LogLevel.Warn);
        }
        else
        {
            host.Write(Tag, "Hosts file is up to date.");
        }
    }

    /// <summary>Asks Windows to trust the local certificate authority, unless it already does.</summary>
    /// <param name="host">Receives the log lines.</param>
    /// <returns>A task that completes when the authority is trusted or the user declined.</returns>
    public static async Task TrustAuthorityAsync(IServiceHost host)
    {
        bool isTrusted;
        try
        {
            isTrusted = await Task.Run(() =>
            {
                using X509Certificate2 authority = LocalCertificates.LoadOrCreateAuthority();
                return LocalCertificates.IsTrusted(authority) || LocalCertificates.Trust(authority);
            });
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            host.Write(Tag, "Trusting the local certificate authority failed: " + ex.Message, LogLevel.Warn);
            return;
        }

        if (!isTrusted)
        {
            host.Write(Tag,
                "The local certificate authority isn't trusted, so browsers will warn about the HTTPS sites. " +
                "Use \"Trust certificate\" in Settings to try again.",
                LogLevel.Warn);
        }
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostName();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotHostCharacter();
}
