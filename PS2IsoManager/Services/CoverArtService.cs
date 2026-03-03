using System.IO;
using System.Net.Http;

namespace PS2IsoManager.Services;

public class CoverArtService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static CoverArtService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PS2IsoManager/1.0");
    }

    // Base URL templates — {0} is replaced with the game ID variant
    private static readonly string[] CoverSources =
    {
        "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{0}.jpg",
        "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/{0}.png",
    };

    public static async Task<string?> DownloadCoverAsync(string gameId, string outputDir, IProgress<string>? status = null, CancellationToken ct = default)
    {
        string artDir = Path.Combine(outputDir, "ART");
        Directory.CreateDirectory(artDir);

        // Check if already exists
        string? existing = FindExistingCover(gameId, outputDir);
        if (existing != null)
            return existing;

        // Build all ID variants to try
        string[] idVariants = GetIdVariants(gameId);

        // Try each variant against each source
        foreach (string variant in idVariants)
        {
            foreach (string sourceTemplate in CoverSources)
            {
                ct.ThrowIfCancellationRequested();

                string url = string.Format(sourceTemplate, variant);
                bool isPng = url.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                string savePath = Path.Combine(artDir, $"{gameId}_COV{(isPng ? ".png" : ".jpg")}");

                status?.Report($"Trying {variant}...");

                string? result = await TryDownloadAsync(url, savePath, ct);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Generate all common ID format variants from a game ID.
    /// e.g. SCES_516.08 → [SCES_516.08, SCES-51608, SCES_51608, SCES 516.08]
    /// </summary>
    private static string[] GetIdVariants(string gameId)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gameId };

        // Remove dots: SCES_516.08 → SCES_51608
        string noDots = gameId.Replace(".", "");
        variants.Add(noDots);

        // Underscore to hyphen: SCES_516.08 → SCES-516.08
        string hyphen = gameId.Replace('_', '-');
        variants.Add(hyphen);

        // Underscore to hyphen + no dots: SCES-51608
        string hyphenNoDots = noDots.Replace('_', '-');
        variants.Add(hyphenNoDots);

        // Lowercase variants
        variants.Add(gameId.ToLowerInvariant());
        variants.Add(hyphenNoDots.ToLowerInvariant());

        return variants.ToArray();
    }

    private static async Task<string?> TryDownloadAsync(string url, string savePath, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            // Verify it's actually an image (not an HTML error page)
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.StartsWith("image/"))
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // Sanity check: at least 1KB (not a tiny error response)
            if (bytes.Length < 1024)
                return null;

            await File.WriteAllBytesAsync(savePath, bytes, ct);
            return savePath;
        }
        catch
        {
            return null;
        }
    }

    public static string? FindExistingCover(string gameId, string outputDir)
    {
        string artDir = Path.Combine(outputDir, "ART");
        if (!Directory.Exists(artDir))
            return null;

        string[] suffixes = { "_COV.jpg", "_COV.png", "_COV.bmp", "_COV.jpeg" };
        foreach (var suffix in suffixes)
        {
            string path = Path.Combine(artDir, gameId + suffix);
            if (File.Exists(path))
                return path;
        }

        // Also try case-insensitive search for the game ID
        try
        {
            var files = Directory.GetFiles(artDir, $"{gameId}_COV.*");
            if (files.Length > 0)
                return files[0];
        }
        catch { }

        return null;
    }
}
