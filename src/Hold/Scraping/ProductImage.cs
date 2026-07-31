namespace Hold.Scraping;

public static class ProductImage
{
    public const int ThumbnailWidth = 160;

    public const int StripWidth = 420;

    public const int CardWidth = 480;

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

    private static bool IsShopify(Uri uri) =>
        uri.Host.Equals("cdn.shopify.com", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.StartsWith("/cdn/shop/", StringComparison.OrdinalIgnoreCase);
}
