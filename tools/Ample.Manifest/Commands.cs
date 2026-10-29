using Ample.Core;
using Ample.Stack;

namespace Ample.ManifestTool;

/// <summary>The refresh, verify and bundle commands.</summary>
public static class Commands
{
    /// <summary>Runs the command named by the arguments.</summary>
    /// <param name="args">The command line: refresh | verify | bundle DIR, optionally followed by
    /// --manifest PATH.</param>
    /// <returns>0 on success, 1 on a failure, 2 on bad arguments.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        string? command = args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        try
        {
            string manifestPath = OptionValue(args, "--manifest") ?? FindManifest();
            switch (command)
            {
                case "refresh":
                    return await RefreshAsync(manifestPath);
                case "verify":
                    return await VerifyAsync(manifestPath);
                case "bundle":
                    string? folder = args.SkipWhile(argument => argument != "bundle").Skip(1).FirstOrDefault();
                    if (folder is null)
                    {
                        Console.Error.WriteLine("bundle needs a target folder.");
                        return 2;
                    }
                    return await BundleAsync(manifestPath, folder);
                default:
                    Console.Error.WriteLine("Usage: Ample.Manifest (refresh | verify | bundle DIR) [--manifest PATH]");
                    return 2;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine("Failed: " + ex.Message);
            return 1;
        }
    }

    /// <summary>Looks up the newest versions, downloads each file and writes the manifest with fresh
    /// checksums.</summary>
    private static async Task<int> RefreshAsync(string manifestPath)
    {
        Manifest manifest = Manifest.Load(manifestPath);
        Console.WriteLine($"Refreshing {manifestPath}");

        foreach (ManifestComponent component in manifest.Components)
        {
            string? newUrl = await Feeds.LatestUrlAsync(component);
            if (newUrl is not null && newUrl != component.Url)
            {
                string? newVersion = Feeds.VersionFromUrl(component, newUrl);
                Console.WriteLine($"  {component.Name}: {component.Version} -> {newVersion ?? "?"}");
                component.Url = newUrl;
                component.Version = newVersion ?? component.Version;
                component.Sha256 = component.Id == "vcredist" ? component.Sha256 : "";
            }
        }

        string cache = CacheFolder();
        foreach (ManifestComponent component in manifest.Components)
        {
            (string sha256, long size) = await DownloadAndHashAsync(component, cache);
            if (component.Id != "vcredist")
            {
                component.Sha256 = sha256;
            }
            component.Size = size;
            Console.WriteLine($"  {component.Name} {component.Version}  {FileTools.FormatSize(size)}  {sha256[..16]}…");
        }

        manifest.Updated = DateTime.UtcNow.ToString("yyyy-MM-dd");
        List<string> problems = manifest.Validate();
        if (problems.Count > 0)
        {
            problems.ForEach(problem => Console.Error.WriteLine("  ! " + problem));
            return 1;
        }

        File.WriteAllText(manifestPath, manifest.ToJson());
        Console.WriteLine("Manifest written.");
        return 0;
    }

    /// <summary>Downloads every file and compares its checksum with the manifest.</summary>
    private static async Task<int> VerifyAsync(string manifestPath)
    {
        Manifest manifest = Manifest.Load(manifestPath);
        List<string> problems = manifest.Validate();
        problems.ForEach(problem => Console.Error.WriteLine("  ! " + problem));

        string cache = CacheFolder();
        foreach (ManifestComponent component in manifest.Components)
        {
            (string sha256, _) = await DownloadAndHashAsync(component, cache);
            bool matches = component.Sha256 is null
                || sha256.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"  {(matches ? "ok  " : "FAIL")} {component.Name} {component.Version}");
            if (!matches)
            {
                problems.Add($"{component.Name} {component.Version}: expected {component.Sha256}, got {sha256}.");
            }
        }

        Console.WriteLine(problems.Count == 0 ? "Every file matches." : $"{problems.Count} problem(s).");
        return problems.Count == 0 ? 0 : 1;
    }

    /// <summary>Downloads every file into a folder, with a copy of the manifest, for the offline installer.</summary>
    private static async Task<int> BundleAsync(string manifestPath, string folder)
    {
        Manifest manifest = Manifest.Load(manifestPath);
        Directory.CreateDirectory(folder);
        foreach (ManifestComponent component in manifest.Components)
        {
            (string sha256, _) = await DownloadAndHashAsync(component, folder);
            if (component.Sha256 is not null && !sha256.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"  ! {component.FileName} does not match the manifest; refresh first.");
                return 1;
            }
            Console.WriteLine($"  {component.FileName}");
        }

        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest.ToJson());
        Console.WriteLine($"Bundle written to {folder}.");
        return 0;
    }

    /// <summary>Downloads a component's file into a folder (reusing a complete earlier copy) and hashes it.</summary>
    private static async Task<(string Sha256, long Size)> DownloadAndHashAsync(
        ManifestComponent component,
        string folder)
    {
        string file = Path.Combine(folder, component.FileName);
        bool reusable = File.Exists(file)
            && (component.Size == 0 || new FileInfo(file).Length == component.Size)
            && await Downloads.MatchesAsync(file, component.Sha256);
        if (!reusable)
        {
            Console.Write($"  downloading {component.FileName} ");
            await Downloads.DownloadAsync(component.Url, file, fraction =>
            {
                if (fraction is { } done && (int)(done * 10) % 2 == 0)
                {
                    Console.Write('.');
                }
            });
            Console.WriteLine();
        }

        return (await Downloads.Sha256Async(file), new FileInfo(file).Length);
    }

    /// <summary>manifest/manifest.json, found by walking up from the current folder.</summary>
    private static string FindManifest()
    {
        DirectoryInfo? folder = new(Directory.GetCurrentDirectory());
        while (folder is not null)
        {
            string candidate = Path.Combine(folder.FullName, "manifest", "manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            folder = folder.Parent;
        }
        throw new IOException("No manifest/manifest.json found above the current folder; pass --manifest PATH.");
    }

    /// <summary>A folder for downloads that survive between runs.</summary>
    private static string CacheFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ample-manifest-cache");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string? OptionValue(string[] args, string option)
    {
        int index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
