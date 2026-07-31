using System.Net.Mime;
using System.Text.Json;

namespace Hold.Scraping;

public sealed class ShopifyParser : IProductParser
{
    public string Name => ScrapeOutcome.ShopifyName;

    public bool StrongSource => true;

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
            null,
            [Name]);
    }

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

    private static string Absolute(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? $"https:{url}" : url;

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
