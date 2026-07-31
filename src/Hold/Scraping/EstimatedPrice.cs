namespace Hold.Scraping;

public static class EstimatedPrice
{
    private const string PinterestLabel = "pinterest";
    private const string PinterestShortHost = "pin.it";

    public static bool LikelyEstimate(string? url)
    {
        var trimmed = url?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return IsPinterest(uri);
    }

    public static bool IsPinterest(Uri uri)
    {
        var host = uri.Host;

        if (host.Equals(PinterestShortHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return host
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Any(label => label.Equals(PinterestLabel, StringComparison.OrdinalIgnoreCase));
    }
}
