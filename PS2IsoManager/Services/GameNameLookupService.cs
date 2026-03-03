using System.Net.Http;
using System.Text.RegularExpressions;

namespace PS2IsoManager.Services;

public static class GameNameLookupService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    static GameNameLookupService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("PS2IsoManager/1.0");
    }

    /// <summary>
    /// Look up the official game name from PSX Data Center using the game ID.
    /// </summary>
    public static async Task<string?> LookupAsync(string gameId, CancellationToken ct = default)
    {
        // Build all ID variants to try
        string[] variants = GetIdVariants(gameId);

        foreach (string variant in variants)
        {
            ct.ThrowIfCancellationRequested();

            string? name = await TryPsxDataCenterAsync(variant, ct);
            if (name != null)
                return name;
        }

        return null;
    }

    private static async Task<string?> TryPsxDataCenterAsync(string gameId, CancellationToken ct)
    {
        try
        {
            string url = $"https://psxdatacenter.com/psx2/games2/{gameId}.html";
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            string html = await response.Content.ReadAsStringAsync(ct);

            // The page title contains the game name, format: "GAME NAME - (REGION)"
            var titleMatch = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
            if (titleMatch.Success)
            {
                string title = titleMatch.Groups[1].Value.Trim();

                // Remove region suffix like " - (PAL)" or " - (NTSC-U/C)"
                title = Regex.Replace(title, @"\s*-\s*\((?:PAL|NTSC[^)]*)\)\s*$", "", RegexOptions.IgnoreCase);

                // Remove site name suffix if present
                title = Regex.Replace(title, @"\s*[-|]\s*PSX\s*Data\s*Center.*$", "", RegexOptions.IgnoreCase);

                title = title.Trim();

                if (!string.IsNullOrEmpty(title) && title.Length <= 32)
                    return title;

                // Truncate to 32 chars if needed (OPL limit)
                if (title.Length > 32)
                    return title.Substring(0, 32).TrimEnd();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string[] GetIdVariants(string gameId)
    {
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Original: SCES_516.08
        variants.Add(gameId);

        // Hyphen + no dots: SCES-51608 (PSX Data Center format)
        string hyphenNoDots = gameId.Replace(".", "").Replace('_', '-');
        variants.Add(hyphenNoDots);

        // Underscore + no dots: SCES_51608
        string noDots = gameId.Replace(".", "");
        variants.Add(noDots);

        // Hyphen + dots: SCES-516.08
        string hyphen = gameId.Replace('_', '-');
        variants.Add(hyphen);

        return variants.ToArray();
    }
}
