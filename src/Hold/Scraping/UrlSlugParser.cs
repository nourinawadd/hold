using System.Globalization;
using System.Text.RegularExpressions;

namespace Hold.Scraping;

/// <summary>
/// The last resort, and the only strategy that cannot fail for lack of a page: shops
/// slugify the product name into the URL, so the link the user pasted already carries a
/// usable title.
///
/// <para>
/// This is what answers a shop that blocks readers outright. No request is made, so a 403,
/// a bot wall, or a dead host changes nothing. It runs even when every fetch failed.
/// </para>
/// </summary>
public sealed partial class UrlSlugParser : IProductParser
{
    /// <summary>Trailing product codes: "-l07230312", "-p04174168", "-42553859211360".</summary>
    [GeneratedRegex(@"-[a-z]?\d{5,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductCode { get; }

    /// <summary>A trailing variant counter: "-2" in "long-scoop-neck-slip-black-2".</summary>
    [GeneratedRegex(@"-\d{1,4}$")]
    private static partial Regex VariantNumber { get; }

    [GeneratedRegex(@"^(?:[a-z]{2}|[a-z]{2}-[a-z]{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex LocaleSegment { get; }

    /// <summary>Host labels that name a platform rather than a shop.</summary>
    private static readonly HashSet<string> NotBrands =
        ["shop", "store", "www", "shopify", "myshopify", "squarespace", "bigcartel", "us", "eu"];

    public string Name => ScrapeOutcome.UrlName;

    /// <summary>Never — this is inference from a URL, not a reading of a page.</summary>
    public bool StrongSource => false;

    public Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        var title = TitleFrom(context.Url);

        if (title is null)
        {
            return Task.FromResult<ProductInfo?>(null);
        }

        return Task.FromResult<ProductInfo?>(new ProductInfo(
            context.Url.ToString(),
            title,
            BrandFrom(context.Url),
            // A URL says nothing about price, currency, or which image is the product.
            null,
            null,
            null,
            [ScrapeOutcome.UrlName]));
    }

    /// <summary>
    /// Null when the slug is opaque. "productpage.1227154001" is not a product name, and
    /// offering it as one is worse than offering nothing.
    /// </summary>
    public static string? TitleFrom(Uri url)
    {
        var segments = url.AbsolutePath.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return null;
        }

        // Retailers commonly end the path with a bare product id — ".../black-leather-bag/1234567".
        // When the last segment is only digits, the name is the one before it.
        var index = segments.Length - 1;

        if (segments.Length > 1 && segments[^1].All(char.IsDigit))
        {
            index--;
        }

        var slug = segments[index];

        // Strip a file extension, but only a real one — a dot inside an id is not.
        var extension = Path.GetExtension(slug);

        if (extension is ".html" or ".htm" or ".php" or ".aspx" or ".js" or ".json")
        {
            slug = slug[..^extension.Length];
        }

        slug = ProductCode.Replace(slug, string.Empty);
        slug = VariantNumber.Replace(slug, string.Empty);

        var words = slug
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 0)
            .ToArray();

        // Every word must be a word. One numeric or dotted token and the slug is an
        // identifier, not a name.
        if (words.Length < 2 || !words.All(word => word.All(char.IsLetter)))
        {
            return null;
        }

        // A path that is only a locale ("/eg/en") carries no product.
        if (words.All(word => LocaleSegment.IsMatch(word)))
        {
            return null;
        }

        var text = CultureInfo.InvariantCulture.TextInfo;

        return string.Join(' ', words.Select(word =>
            word.Length <= 2 ? word.ToUpperInvariant() : text.ToTitleCase(word.ToLowerInvariant())));
    }

    /// <summary>
    /// The domain, cleaned up. Often right for a single-brand shop and often wrong for a
    /// retailer — which is why it renders faint and sits in an editable field.
    /// </summary>
    public static string? BrandFrom(Uri url)
    {
        var labels = url.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var label = labels
            .FirstOrDefault(part => !NotBrands.Contains(part.ToLowerInvariant()) && part.Length > 2);

        if (label is null)
        {
            return null;
        }

        // "shopdoen.com" is Dôen with a platform prefix stuck on the front.
        if (label.StartsWith("shop", StringComparison.OrdinalIgnoreCase) && label.Length > 7)
        {
            label = label[4..];
        }

        return label.All(char.IsLetter)
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.ToLowerInvariant())
            : null;
    }
}
