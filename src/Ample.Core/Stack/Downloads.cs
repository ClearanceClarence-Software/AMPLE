using System.Security.Cryptography;

namespace Ample.Stack;

/// <summary>Downloads files and checks their checksums.</summary>
public static class Downloads
{
    /// <summary>
    /// The shared client for the manifest and the component downloads; its 30-minute timeout suits large zips.
    /// </summary>
    public static readonly HttpClient Http = CreateClient();

    /// <summary>Creates the shared client with a long timeout and a user agent naming AMPLE and its version.</summary>
    /// <returns>A client ready for both small requests and zip downloads.</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        string version = typeof(Downloads).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"AMPLE/{version} (+https://github.com/ClearanceClarence-Software/AMPLE)");
        return client;
    }

    /// <summary>Downloads a file through a .part file that replaces the destination only once complete.</summary>
    /// <param name="url">Absolute URL of the file.</param>
    /// <param name="destination">Full path of the file to write; its folder is created, a file there replaced.</param>
    /// <param name="progress">
    /// Receives null when the size is unknown or 0 when known, then fractions 0..1 about every 512 KB, then 1.
    /// </param>
    /// <param name="ct">Cancels the request and the copy; checked on every read and write.</param>
    /// <returns>A task that completes when the destination holds the whole file.</returns>
    /// <exception cref="HttpRequestException">The request fails or the server answers with an error status.</exception>
    public static async Task DownloadAsync(
        string url,
        string destination,
        Action<double?> progress,
        CancellationToken ct = default)
    {
        string destinationFolder = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("The destination must be a file inside a folder.", nameof(destination));
        Directory.CreateDirectory(destinationFolder);
        string partialFile = destination + ".part";

        using (HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long? totalBytes = response.Content.Headers.ContentLength;
            progress(totalBytes > 0 ? 0 : null);

            await using Stream source = await response.Content.ReadAsStreamAsync(ct);
            await using FileStream target = File.Create(partialFile);
            var buffer = new byte[128 * 1024];
            long bytesWritten = 0;
            long bytesReported = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                bytesWritten += bytesRead;

                // Reporting every 512 KB keeps the UI updates cheap on a fast connection.
                if (totalBytes > 0 && bytesWritten - bytesReported > 512 * 1024)
                {
                    bytesReported = bytesWritten;
                    progress(bytesWritten / (double)totalBytes);
                }
            }
        }

        File.Move(partialFile, destination, overwrite: true);
        progress(1);
    }

    /// <summary>Computes a file's SHA-256 checksum.</summary>
    /// <param name="file">Full path of the file to hash.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The checksum as 64 lowercase hex characters.</returns>
    public static async Task<string> Sha256Async(string file, CancellationToken ct = default)
    {
        await using FileStream stream = File.OpenRead(file);
        byte[] hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Whether a file's checksum matches the expected one.</summary>
    /// <param name="file">Full path of the file.</param>
    /// <param name="expectedSha256">The expected checksum, hex in either case; null or blank matches nothing.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>True when the file exists and hashes to the expected value.</returns>
    public static async Task<bool> MatchesAsync(string file, string? expectedSha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(file))
        {
            return false;
        }

        string actual = await Sha256Async(file, ct);
        return actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
