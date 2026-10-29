using Ample.Core;
using Ample.Services;

namespace Ample.Stack;

/// <summary>Where a component's archive came from.</summary>
/// <param name="Path">Full path of the verified archive.</param>
/// <param name="Source">"bundle" when it shipped with the offline installer; "download" otherwise.</param>
/// <param name="Sha256">The archive's checksum; empty when the manifest had none to check against.</param>
public sealed record AcquiredFile(string Path, string Source, string Sha256);

/// <summary>
/// Gets a component's archive: from the bundle folder next to AMPLE.exe when the offline installer put it there,
/// else from a previous download in tmp, else by downloading it. Every file is checked against the manifest's
/// SHA-256 before it is used.
/// </summary>
/// <param name="host">AMPLE: paths and the log.</param>
public sealed class ComponentSource(IServiceHost host)
{
    private const string Tag = "install";

    /// <summary>The folder downloads are kept in, so a failed install can resume without downloading again.</summary>
    public string DownloadFolder => Path.Combine(host.Paths.Tmp, "downloads");

    /// <summary>Finds or downloads the component's file and verifies it.</summary>
    /// <param name="component">The component.</param>
    /// <param name="progress">Receives the download progress; nothing when a verified file was already there.</param>
    /// <param name="ct">Cancels a download.</param>
    /// <returns>The verified file.</returns>
    /// <exception cref="HttpRequestException">The download fails.</exception>
    /// <exception cref="InvalidDataException">The downloaded file does not match the manifest's checksum.</exception>
    public async Task<AcquiredFile> AcquireAsync(
        ManifestComponent component,
        InstallProgress progress,
        CancellationToken ct)
    {
        string bundled = Path.Combine(host.Paths.Bundle, component.FileName);
        if (await Downloads.MatchesAsync(bundled, component.Sha256, ct))
        {
            host.Debug(Tag, $"Using {component.FileName} from the bundle folder.");
            return new AcquiredFile(bundled, "bundle", component.Sha256!);
        }

        string downloaded = Path.Combine(DownloadFolder, component.FileName);
        if (await Downloads.MatchesAsync(downloaded, component.Sha256, ct))
        {
            host.Debug(Tag, $"Using the earlier download of {component.FileName}.");
            return new AcquiredFile(downloaded, "download", component.Sha256!);
        }

        string site = Uri.TryCreate(component.Url, UriKind.Absolute, out Uri? uri) ? uri.Host : component.Url;
        host.Write(Tag, $"Downloading {component.Name} {component.Version} from {site}");
        progress.Detail(component.Size > 0 ? FileTools.FormatSize(component.Size) : "");
        await Downloads.DownloadAsync(component.Url, downloaded, fraction =>
        {
            progress.Fraction(fraction);
            if (fraction is { } done && component.Size > 0)
            {
                progress.Detail(
                    $"{FileTools.FormatSize((long)(done * component.Size))} of {FileTools.FormatSize(component.Size)}");
            }
        }, ct);

        if (component.Sha256 is null)
        {
            host.Write(
                Tag,
                $"{component.FileName} has no checksum in the manifest, so it was not verified.",
                LogLevel.Warn);
            return new AcquiredFile(downloaded, "download", "");
        }

        string actual = await Downloads.Sha256Async(downloaded, ct);
        if (!actual.Equals(component.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            FileTools.TryDeleteFile(downloaded);
            throw new InvalidDataException(
                $"{component.FileName} does not match the manifest's checksum "
                + $"(expected {component.Sha256[..12]}…, got {actual[..12]}…). The download was deleted.");
        }

        host.Debug(Tag, $"{component.FileName} verified ({actual[..12]}…).");
        return new AcquiredFile(downloaded, "download", actual);
    }
}
