using System.Net.Mime;
using System.Text.Json;

namespace Hold.Scraping;

/// <summary>
/// Shopify exposes a clean JSON version of any product at the same path with ".js"
/// appended. It covers a large share of independent fashion labels, costs about 8KB
/// against a megabyte of rendered page, and never lies about which variant you are looking
/// at — which is why it runs first.
/// </summary>
public sealed class ShopifyParser : IProductParser
{
    public string Name => ScrapeOutcome.ShopifyName;

    public bool StrongSource => true;

    /// <summary>True for /products/{handle}, which is the only shape worth trying.</summary>
    public static bool Matches(Uri url)
    {
        var segments = url.AbsolutePath.Trim('/').Split('/');

        return segments.Length >= 2
            && segments[^2].Equals("products", StringComparison.OrdinalIgnoreCase)
            && segments[^1].Length > 0
            && !segments[^1].EndsWith(".js", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        if (!Matches(context.Url))
        {
            return null;
        }

        var builder = new UriBuilder(context.Url) { Path = context.Url.AbsolutePath + ".js" };

        using var response = await context.Http.GetAsync(builder.Uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // A storefront that does not know this route often answers 200 with the HTML page.
        // Anything that is not JSON is not ours.
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType is not (MediaTypeNames.Application.Json or "text/javascript" or "application/javascript"))
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = Text(root, "title");
        var vendor = Text(root, "vendor");

        if (title is null)
        {
            return null;
        }

        return new ProductInfo(
            context.Url.ToString(),
            title,
            vendor,
            Image(root),
            Price(root),
            // The .js payload has no currency field; the shop's own currency is assumed and
            // corrected by whatever the later strategies find.
            null,
            [Name]);
    }

    /// <summary>Shopify quotes money in <em>cents</em>. 6800 is sixty-eight dollars.</summary>
    private static decimal? Price(JsonElement root)
    {
        if (!root.TryGetProperty("price", out var price))
        {
            return null;
        }

        return price.ValueKind switch
        {
            JsonValueKind.Number when price.TryGetDecimal(out var cents) => cents / 100m,
            JsonValueKind.String => PriceNormaliser.Parse(price.GetString()) / 100m,
            _ => null,
        };
    }

    private static string? Image(JsonElement root)
    {
        if (Text(root, "featured_image") is { } featured)
        {
            return Absolute(featured);
        }

        if (root.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                if (image.ValueKind == JsonValueKind.String && image.GetString() is { Length: > 0 } value)
                {
                    return Absolute(value);
                }
            }
        }

        return null;
    }

    // Shopify returns protocol-relative URLs like //cdn.shopify.com/...
    private static string Absolute(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
