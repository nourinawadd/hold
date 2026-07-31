using AngleSharp.Dom;

namespace Hold.Scraping;

public sealed record ScrapeContext(Uri Url, IDocument? Document, HttpClient Http);

public interface IProductParser
{
    string Name { get; }

    bool StrongSource { get; }

    Task<ProductInfo?> TryParseAsync(ScrapeContext context, CancellationToken cancellationToken);
}
