using AngleSharp.Dom;

namespace Hold.Scraping;

/// <summary>
/// Page metadata. Almost every shop has og:title and og:image because social previews
/// depend on them, which makes this the strategy that usually rescues a partial read. It
/// is a guess rather than a reading, so its values render faint.
/// </summary>
public sealed class OpenGraphParser : IProductParser
{
    public string Name => ScrapeOutcome.OpenGraphName;

    public bool StrongSource => false;

    public Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is null)
        {
            return Task.FromResult<ProductInfo?>(null);
        }

        var title = Meta(context.Document, "og:title")
            // <title> is the last resort, and often carries the shop name as a suffix.
            ?? Blank(context.Document.Title);

        var amount = Meta(context.Document, "product:price:amount");

        var info = new ProductInfo(
            context.Url.ToString(),
            title,
            Meta(context.Document, "og:site_name"),
            Meta(context.Document, "og:image"),
            PriceNormaliser.Parse(amount),
            Meta(context.Document, "product:price:currency") ?? PriceNormaliser.Currency(amount),
            [ScrapeOutcome.OpenGraphName]);

        // Nothing worth reporting means nothing at all, so the chain keeps going.
        var empty = info is { Title: null, Brand: null, ImageUrl: null, Price: null, Currency: null };

        return Task.FromResult(empty ? null : info);
    }

    /// <summary>
    /// OpenGraph uses property=, but plenty of templates emit name= instead. Both are read.
    /// </summary>
    private static string? Meta(IDocument document, string key)
    {
        var element = document.QuerySelector($"meta[property='{key}']")
            ?? document.QuerySelector($"meta[name='{key}']");

        return Blank(element?.GetAttribute("content"));
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
