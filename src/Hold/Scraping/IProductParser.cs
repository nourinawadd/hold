using AngleSharp.Dom;

namespace Hold.Scraping;

/// <summary>
/// What a parser is given. The document is parsed once and shared, so four strategies cost
/// one parse. It is null when the page was never fetched — the Shopify strategy runs
/// before that and makes its own request.
/// </summary>
public sealed record ScrapeContext(Uri Url, IDocument? Document, HttpClient Http);

/// <summary>
/// One strategy in the chain. Returning null means "nothing here", not "failure" — the
/// next strategy gets its turn either way.
/// </summary>
public interface IProductParser
{
    /// <summary>Shown to the user as provenance, so keep it readable.</summary>
    string Name { get; }

    /// <summary>
    /// True when this strategy reads structured data a shop published deliberately, false
    /// when it guesses from page furniture. Drives whether values render faint.
    /// </summary>
    bool StrongSource { get; }

    Task<ProductInfo?> TryParseAsync(ScrapeContext context, CancellationToken cancellationToken);
}
