using Ample.Core;

namespace Ample.Stack;

/// <summary>What one installer run will install.</summary>
/// <param name="Manifest">The manifest the components come from.</param>
/// <param name="Components">The components to acquire and place, in install order.</param>
/// <param name="DefaultPhp">The PHP version new projects use; null keeps the stack's current default.</param>
public sealed record InstallPlan(Manifest Manifest, IReadOnlyList<ManifestComponent> Components, string? DefaultPhp)
{
    /// <summary>The order components are placed in: Apache before the module that goes into its folder.</summary>
    private static readonly string[] _order =
        ["vcredist", "apache", "mod_fcgid", "php", "mariadb", "ledger", "phpmyadmin"];

    /// <summary>
    /// A first install: everything required, the default MariaDB, the chosen PHP versions, phpMyAdmin when asked for.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="phpVersions">The PHP versions to install; the manifest's default is always among them.</param>
    /// <param name="includePhpMyAdmin">True also installs phpMyAdmin.</param>
    /// <returns>The plan.</returns>
    public static InstallPlan Fresh(Manifest manifest, IReadOnlyCollection<string> phpVersions, bool includePhpMyAdmin)
    {
        ManifestComponent defaultPhp = Manifest.DefaultOf(manifest.PhpVersions)!;
        ManifestComponent defaultMariaDb = Manifest.DefaultOf(manifest.MariaDbVersions)!;
        var wantedPhp = new HashSet<string>(phpVersions, StringComparer.OrdinalIgnoreCase) { defaultPhp.Version };

        List<ManifestComponent> components = manifest.Components
            .Where(component => component.Id.ToLowerInvariant() switch
            {
                "php" => wantedPhp.Contains(component.Version),
                "mariadb" => component.Version == defaultMariaDb.Version,
                _ => !component.Optional || (includePhpMyAdmin && IsPhpMyAdmin(component)),
            })
            .ToList();
        return new InstallPlan(manifest, Sorted(components), defaultPhp.Version);
    }

    /// <summary>
    /// A repair: every required component the stack does not record as installed, or records but whose files are
    /// gone.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="stack">The installed state.</param>
    /// <param name="paths">The stack's folders, checked for each component's marker file.</param>
    /// <returns>The plan; its component list is empty when nothing is missing.</returns>
    public static InstallPlan Missing(Manifest manifest, StackState stack, AmplePaths paths)
    {
        ManifestComponent defaultPhp = Manifest.DefaultOf(manifest.PhpVersions)!;
        ManifestComponent defaultMariaDb = Manifest.DefaultOf(manifest.MariaDbVersions)!;
        bool IsInstalled(ManifestComponent component) =>
            stack.Has(component.StackId) && StackInstaller.IsPlaced(paths, component);
        bool hasAnyPhp = manifest.PhpVersions.Any(IsInstalled);
        bool hasAnyMariaDb = manifest.MariaDbVersions.Any(IsInstalled);

        List<ManifestComponent> components = manifest.Components
            .Where(component => !IsInstalled(component))
            .Where(component => component.Id.ToLowerInvariant() switch
            {
                "php" => !hasAnyPhp && component.Version == defaultPhp.Version,
                "mariadb" => !hasAnyMariaDb && component.Version == defaultMariaDb.Version,
                _ => !component.Optional,
            })
            .ToList();
        return new InstallPlan(manifest, Sorted(components), stack.DefaultPhp ?? defaultPhp.Version);
    }

    /// <summary>One component on its own, such as phpMyAdmin or another PHP version.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="component">The component to install.</param>
    /// <returns>The plan.</returns>
    public static InstallPlan ForComponent(Manifest manifest, ManifestComponent component) =>
        new(manifest, [component], null);

    /// <summary>True for the phpMyAdmin component.</summary>
    /// <param name="component">Any component.</param>
    /// <returns>True when its id is "phpmyadmin".</returns>
    public static bool IsPhpMyAdmin(ManifestComponent component) =>
        component.Id.Equals("phpmyadmin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Components in install order; unknown ids go last in manifest order.</summary>
    private static List<ManifestComponent> Sorted(IEnumerable<ManifestComponent> components) =>
        components
            .Select((component, index) => (component, index))
            .OrderBy(entry => OrderOf(entry.component.Id))
            .ThenBy(entry => entry.index)
            .Select(entry => entry.component)
            .ToList();

    private static int OrderOf(string id)
    {
        int index = Array.FindIndex(_order, known => known.Equals(id, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? _order.Length : index;
    }
}

/// <summary>Where the installer reports progress; every callback runs on the installer's thread.</summary>
/// <param name="Step">A new step started, e.g. "Downloading MariaDB 11.4.5".</param>
/// <param name="Detail">A line under the step, e.g. "34.2 MB of 68.0 MB".</param>
/// <param name="Fraction">Progress 0..1 within the step, or null for busy with no percentage.</param>
public sealed record InstallProgress(Action<string> Step, Action<string> Detail, Action<double?> Fraction)
{
    /// <summary>A reporter that ignores everything, for tests and command lines.</summary>
    public static InstallProgress None { get; } = new(_ => { }, _ => { }, _ => { });
}

/// <summary>How an installer run ended.</summary>
/// <param name="Success">True when every component was placed and configured.</param>
/// <param name="Message">One sentence for the status line; the details are in the log.</param>
public sealed record InstallResult(bool Success, string Message);
