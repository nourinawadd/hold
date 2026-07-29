using System.Text;
using System.Text.Json;

namespace Hold.Scraping;

/// <summary>
/// schema.org Product blocks. The richest source after Shopify, and the one most shops
/// publish deliberately for search engines — which is why it is trusted in full ink.
/// </summary>
public sealed class JsonLdParser : IProductParser
{
    public string Name => ScrapeOutcome.JsonLdName;

    public bool StrongSource => true;

    public Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is null)
        {
            return Task.FromResult<ProductInfo?>(null);
        }

        foreach (var script in context.Document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            // Malformed blocks are common and must not throw. A shop with one broken block
            // often has three good ones.
            var document = Parse(script.TextContent);

            if (document is null)
            {
                continue;
            }

            using (document)
            {
                foreach (var node in Flatten(document.RootElement))
                {
                    if (IsProduct(node) && Read(node, context.Url) is { } info)
                    {
                        return Task.FromResult<ProductInfo?>(info);
                    }
                }
            }
        }

        return Task.FromResult<ProductInfo?>(null);
    }

    /// <summary>
    /// Strict first, then one repair attempt.
    /// <para>
    /// The repair exists because of a real page: Naadam renders its product description
    /// straight into the JSON, newlines and all, and a literal newline inside a string is
    /// invalid JSON. System.Text.Json refuses it, and refusing with it would throw away the
    /// only structured product data on the page. Escaping the control characters recovers
    /// the block without loosening anything else.
    /// </para>
    /// </summary>
    private static JsonDocument? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            // Fall through to the repair.
        }

        try
        {
            return JsonDocument.Parse(EscapeControlCharacters(raw));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Escapes raw control characters, but only where they sit inside a string.</summary>
    private static string EscapeControlCharacters(string raw)
    {
        var builder = new StringBuilder(raw.Length + 64);
        var inString = false;
        var escaped = false;

        foreach (var character in raw)
        {
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
                continue;
            }

            if (inString && character == '\\')
            {
                builder.Append(character);
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                builder.Append(character);
                continue;
            }

            if (inString && character < 0x20)
            {
                builder.Append(character switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => $"\\u{(int)character:x4}",
                });

                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Walks arrays and @graph so a Product nested anywhere is still found.</summary>
    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var inner in Flatten(item))
                {
                    yield return inner;
                }
            }

            yield break;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        yield return element;

        if (element.TryGetProperty("@graph", out var graph))
        {
            foreach (var inner in Flatten(graph))
            {
                yield return inner;
            }
        }
    }

    /// <summary>@type is a string on most sites and an array on some.</summary>
    private static bool IsProduct(JsonElement node)
    {
        if (!node.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => NamesProduct(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray()
                .Any(entry => entry.ValueKind == JsonValueKind.String && NamesProduct(entry.GetString())),
            _ => false,
        };
    }

    /// <summary>
    /// ProductGroup counts. Shopify emits it for anything with variants, and a chain that
    /// only accepted "Product" would read nothing at all from those pages.
    /// </summary>
    private static bool NamesProduct(string? type)
    {
        if (type is null)
        {
            return false;
        }

        var bare = type[(type.LastIndexOf('/') + 1)..];

        return bare.Equals("Product", StringComparison.OrdinalIgnoreCase)
            || bare.Equals("ProductGroup", StringComparison.OrdinalIgnoreCase);
    }

    private static ProductInfo? Read(JsonElement node, Uri pageUrl)
    {
        if (Text(node, "name") is not { } title)
        {
            return null;
        }

        var (price, currency) = Offers(node);
        var image = Image(node);

        // A ProductGroup carries the name and brand but pushes price and image down into
        // hasVariant. The variants' own names are useless here — "Black / XXS" is not what
        // anyone saved — so only the missing pieces are taken.
        if (price is null || image is null)
        {
            foreach (var variant in Variants(node))
            {
                if (price is null)
                {
                    var (variantPrice, variantCurrency) = Offers(variant);

                    if (variantPrice is not null)
                    {
                        price = variantPrice;
                        currency ??= variantCurrency;
                    }
                }

                image ??= Image(variant);

                if (price is not null && image is not null)
                {
                    break;
                }
            }
        }

        return new ProductInfo(
            pageUrl.ToString(),
            title,
            Brand(node),
            image,
            price,
            currency,
            [ScrapeOutcome.JsonLdName]);
    }

    private static IEnumerable<JsonElement> Variants(JsonElement node) =>
        node.TryGetProperty("hasVariant", out var variants) ? Flatten(variants) : [];

    /// <summary>brand is a bare string on some sites and { "name": ... } on others.</summary>
    private static string? Brand(JsonElement node)
    {
        if (!node.TryGetProperty("brand", out var brand))
        {
            return null;
        }

        return brand.ValueKind switch
        {
            JsonValueKind.String => Blank(brand.GetString()),
            JsonValueKind.Object => Text(brand, "name"),
            JsonValueKind.Array => brand.EnumerateArray()
                .Select(entry => entry.ValueKind == JsonValueKind.String ? Blank(entry.GetString()) : Text(entry, "name"))
                .FirstOrDefault(value => value is not null),
            _ => null,
        };
    }

    /// <summary>image is a string, an array of strings, or an object carrying url.</summary>
    private static string? Image(JsonElement node)
    {
        if (!node.TryGetProperty("image", out var image))
        {
            return null;
        }

        return image.ValueKind switch
        {
            JsonValueKind.String => Blank(image.GetString()),
            JsonValueKind.Object => Text(image, "url"),
            JsonValueKind.Array => image.EnumerateArray()
                .Select(entry => entry.ValueKind == JsonValueKind.String ? Blank(entry.GetString()) : Text(entry, "url"))
                .FirstOrDefault(value => value is not null),
            _ => null,
        };
    }

    /// <summary>offers is an object or an array; price wins over lowPrice.</summary>
    private static (decimal? Price, string? Currency) Offers(JsonElement node)
    {
        if (!node.TryGetProperty("offers", out var offers))
        {
            return (null, null);
        }

        foreach (var offer in Flatten(offers))
        {
            var price = Number(offer, "price") ?? Number(offer, "lowPrice");
            var currency = Text(offer, "priceCurrency");

            if (price is not null || currency is not null)
            {
                return (price, currency);
            }
        }

        return (null, null);
    }

    // Prices arrive as numbers and as strings, sometimes in European format.
    private static decimal? Number(JsonElement node, string property)
    {
        if (!node.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String => PriceNormaliser.Parse(value.GetString()),
            _ => null,
        };
    }

    private static string? Text(JsonElement node, string property) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? Blank(value.GetString())
            : null;

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
