using System.Globalization;
using System.Text.RegularExpressions;

namespace Hold.Scraping;

public sealed partial class UrlSlugParser : IProductParser
{
    [GeneratedRegex(@"-[a-z]?\d{5,}$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductCode { get; }

    [GeneratedRegex(@"-\d{1,4}$")]
    private static partial Regex VariantNumber { get; }

    [GeneratedRegex(@"^(?:[a-z]{2}|[a-z]{2}-[a-z]{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex LocaleSegment { get; }

    private static readonly HashSet<string> NotBrands =
        ["shop", "store", "www", "shopify", "myshopify", "squarespace", "bigcartel", "us", "eu"];

    public string Name => ScrapeOutcome.UrlName;

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
            null,
            null,
            null,
            [ScrapeOutcome.UrlName]));
    }

    public static string? TitleFrom(Uri url)
    {
        var segments = url.AbsolutePath.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
        {
            return null;
        }

        var index = segments.Length - 1;

        if (segments.Length > 1 && segments[^1].All(char.IsDigit))
        {
            index--;
        }

        var slug = segments[index];

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

        if (words.Length < 2 || !words.All(word => word.All(char.IsLetter)))
        {
            return null;
        }

        if (words.All(word => LocaleSegment.IsMatch(word)))
        {
            return null;
        }

        var text = CultureInfo.InvariantCulture.TextInfo;

        return string.Join(' ', words.Select(word =>
            word.Length <= 2 ? word.ToUpperInvariant() : text.ToTitleCase(word.ToLowerInvariant())));
    }

    public static string? BrandFrom(Uri url)
    {
        var labels = url.Host.Split('.', StringSplitOptions.RemoveEmptyEntries);

        var label = labels
            .FirstOrDefault(part => !NotBrands.Contains(part.ToLowerInvariant()) && part.Length > 2);

        if (label is null)
        {
            return null;
        }

        if (label.StartsWith("shop", StringComparison.OrdinalIgnoreCase) && label.Length > 7)
        {
            label = label[4..];
        }

        return label.All(char.IsLetter)
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(label.ToLowerInvariant())
            : null;
    }
}
