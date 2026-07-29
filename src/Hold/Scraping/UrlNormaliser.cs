namespace Hold.Scraping;

/// <summary>
/// Strips the tracking noise a link picks up on its way to you, so the same product saved
/// twice looks the same.
/// </summary>
public static class UrlNormaliser
{
    private static readonly string[] DropExact = ["gclid", "fbclid", "msclkid", "srsltid"];
    private static readonly string[] DropPrefix = ["utm_", "mc_"];

    public static string Normalise(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;

        // Anything that is not a URL is handed back untouched; validation is the caller's.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var kept = new List<string>();

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = pair.Split('=', 2)[0];

            if (!ShouldDrop(key))
            {
                kept.Add(pair);
            }
        }

        var builder = new UriBuilder(uri)
        {
            // The fragment never identifies a different product.
            Fragment = string.Empty,
            Query = string.Join('&', kept),
        };

        // UriBuilder writes :443 back into the string otherwise.
        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri.ToString();
    }

    private static bool ShouldDrop(string key) =>
        DropExact.Contains(key, StringComparer.OrdinalIgnoreCase)
        || DropPrefix.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
