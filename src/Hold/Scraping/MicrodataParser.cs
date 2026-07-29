using AngleSharp.Dom;

namespace Hold.Scraping;

/// <summary>
/// itemprop attributes. Last in the chain, and rarely reached: of the six real shops
/// checked while building this, not one emitted a single itemprop. Kept because older
/// storefronts still do, and because it costs nothing once the document is parsed.
/// </summary>
public sealed class MicrodataParser : IProductParser
{
    public string Name => ScrapeOutcome.MicrodataName;

    public bool StrongSource => false;

    public Task<ProductInfo?> TryParseAsync(
        ScrapeContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is null)
        {
            return Task.FromResult<ProductInfo?>(null);
        }

        var price = Value(context.Document, "price");

        var info = new ProductInfo(
            context.Url.ToString(),
            Value(context.Document, "name"),
            Brand(context.Document),
            Value(context.Document, "image"),
            PriceNormaliser.Parse(price),
            Value(context.Document, "priceCurrency") ?? PriceNormaliser.Currency(price),
            [ScrapeOutcome.MicrodataName]);

        var empty = info is { Title: null, Brand: null, ImageUrl: null, Price: null, Currency: null };

        return Task.FromResult(empty ? null : info);
    }

    /// <summary>
    /// The brand is usually a nested scope carrying its own name, so the bare
    /// [itemprop=name] lookup would return the product title again.
    /// </summary>
    private static string? Brand(IDocument document)
    {
        var scope = document.QuerySelector("[itemprop='brand']");

        if (scope is null)
        {
            return null;
        }

        return Read(scope.QuerySelector("[itemprop='name']") ?? scope);
    }

    private static string? Value(IDocument document, string property) =>
        Read(document.QuerySelector($"[itemprop='{property}']"));

    /// <summary>
    /// content= wins where present — a &lt;meta&gt; or a formatted span carries the machine
    /// value there and the human one in its text.
    /// </summary>
    private static string? Read(IElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var content = element.GetAttribute("content");

        if (!string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        // <img itemprop="image"> puts it in src.
        var source = element.GetAttribute("src") ?? element.GetAttribute("href");

        if (!string.IsNullOrWhiteSpace(source))
        {
            return source.Trim();
        }

        return string.IsNullOrWhiteSpace(element.TextContent) ? null : element.TextContent.Trim();
    }
}
