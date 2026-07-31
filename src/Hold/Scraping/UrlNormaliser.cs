namespace Hold.Scraping;

public static class UrlNormaliser
{
    private static readonly string[] DropExact = ["gclid", "fbclid", "msclkid", "srsltid"];
    private static readonly string[] DropPrefix = ["utm_", "mc_"];

    public static string Normalise(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;

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
            Fragment = string.Empty,
            Query = string.Join('&', kept),
        };

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
