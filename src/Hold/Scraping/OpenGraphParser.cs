using AngleSharp.Dom;

namespace Hold.Scraping;

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

        var empty = info is { Title: null, Brand: null, ImageUrl: null, Price: null, Currency: null };

        return Task.FromResult(empty ? null : info);
    }

    private static string? Meta(IDocument document, string key)
    {
        var element = document.QuerySelector($"meta[property='{key}']")
            ?? document.QuerySelector($"meta[name='{key}']");

        return Blank(element?.GetAttribute("content"));
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
