using AngleSharp.Dom;

namespace Hold.Scraping;

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

        var source = element.GetAttribute("src") ?? element.GetAttribute("href");

        if (!string.IsNullOrWhiteSpace(source))
        {
            return source.Trim();
        }

        return string.IsNullOrWhiteSpace(element.TextContent) ? null : element.TextContent.Trim();
    }
}
