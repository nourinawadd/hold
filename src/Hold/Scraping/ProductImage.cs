namespace Hold.Scraping;

/// <summary>
/// Asks a shop's CDN for an image the size it will actually be drawn at.
///
/// <para>
/// Shops serve their originals: a Dôen product photo measured 2048×1807 and 718KB. Drawn
/// into a 73-pixel thumbnail that is a 28× reduction, which browsers alias badly — the
/// picture looks coarse despite being far larger than the slot. Requesting a fitting size
/// fixes the look and takes the download from 718KB to 11KB.
/// </para>
/// </summary>
public static class ProductImage
{
    /// <summary>Roughly twice the drawn size, so the picture stays sharp on a dense screen.</summary>
    public const int ThumbnailWidth = 160;

    /// <summary>For an item card and the add panel's preview.</summary>
    public const int CardWidth = 480;

    /// <summary>
    /// Parameter names CDNs use for width. Rewriting one that is already present is safe —
    /// the shop put it there, so the endpoint understands it.
    /// </summary>
    private static readonly string[] WidthParameters = ["width", "w", "imwidth", "sw", "wid"];

    public static string? AtWidth(string? url, int width)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var pairs = new List<KeyValuePair<string, string>>();
        var replaced = false;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var name = parts[0];

            if (!replaced && WidthParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                pairs.Add(new(name, width.ToString()));
                replaced = true;
                continue;
            }

            pairs.Add(new(name, parts.Length > 1 ? parts[1] : string.Empty));
        }

        if (!replaced)
        {
            // No width to rewrite. Adding one is only safe where the convention is known;
            // an unrecognised CDN might treat an unknown parameter as a cache-buster or a
            // signature mismatch, so its URL is left exactly as the shop gave it.
            if (!IsShopify(uri))
            {
                return url;
            }

            pairs.Add(new("width", width.ToString()));
        }

        return new UriBuilder(uri)
        {
            Query = string.Join('&', pairs.Select(pair =>
                pair.Value.Length == 0 ? pair.Key : $"{pair.Key}={pair.Value}")),
        }.Uri.ToString();
    }

    /// <summary>Shopify serves images from two shapes, both of which honour ?width=.</summary>
    private static bool IsShopify(Uri uri) =>
        uri.Host.Equals("cdn.shopify.com", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.StartsWith("/cdn/shop/", StringComparison.OrdinalIgnoreCase);
}
