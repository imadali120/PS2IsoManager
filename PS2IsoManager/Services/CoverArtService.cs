using System.IO;
using System.Net.Http;

namespace PS2IsoManager.Services;

public class CoverArtService
{
    private static readonly HttpClient Http = new();

    // Primary source: xlenore's ps2-covers GitHub repo
    private const string BaseUrl = "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default";

    public static async Task<string?> DownloadCoverAsync(string gameId, string outputDir, CancellationToken ct = default)
    {
        string artDir = Path.Combine(outputDir, "ART");
        Directory.CreateDirectory(artDir);

        string covPath = Path.Combine(artDir, $"{gameId}_COV.jpg");
        if (File.Exists(covPath))
            return covPath;

        // Try with the game ID as-is
        string? result = await TryDownloadAsync($"{BaseUrl}/{gameId}.jpg", covPath, ct);
        if (result != null) return result;

        // Try with underscore replaced by hyphen
        string altId = gameId.Replace('_', '-');
        result = await TryDownloadAsync($"{BaseUrl}/{altId}.jpg", covPath, ct);
        if (result != null) return result;

        // Try PNG variant
        string covPngPath = Path.Combine(artDir, $"{gameId}_COV.png");
        result = await TryDownloadAsync($"{BaseUrl}/{gameId}.png", covPngPath, ct);
        return result;
    }

    private static async Task<string?> TryDownloadAsync(string url, string savePath, CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
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

        string[] extensions = { "_COV.jpg", "_COV.png", "_COV.bmp" };
        foreach (var ext in extensions)
        {
            string path = Path.Combine(artDir, gameId + ext);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
