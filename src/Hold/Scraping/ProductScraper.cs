using System.Diagnostics;
using System.Net;
using AngleSharp.Html.Parser;

namespace Hold.Scraping;

/// <summary>
/// Runs the strategy chain and merges what comes back. Never throws for a page it could not
/// read: the add flow must always fall through to manual entry, so every failure returns a
/// sentence instead.
/// </summary>
public sealed class ProductScraper(HttpClient http)
{
    // The fall-through line from the spec. Every refusal ends the same way, because the
    // user's next move is the same in every case.
    private const string FallThrough = "Fill in what you know — the link is saved.";

    private readonly IProductParser[] parsers =
    [
        new ShopifyParser(),
        new JsonLdParser(),
        new OpenGraphParser(),
        new MicrodataParser(),
    ];

    public async Task<ScrapeOutcome> ReadAsync(
        string rawUrl,
        IProgress<ScrapeStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalised = UrlNormaliser.Normalise(rawUrl);

        if (!Uri.TryCreate(normalised, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return Refused(normalised, "That does not look like a web link.");
        }

        progress?.Report(new ScrapeStep($"Reading {url.Host}"));

        var clock = Stopwatch.StartNew();
        var merged = new Merge(normalised);
        string? failure = null;

        try
        {
            // 1 — Shopify, which answers in about 8KB where the rendered page costs a
            // megabyte. Worth trying before anything is downloaded.
            if (ShopifyParser.Matches(url))
            {
                progress?.Report(new ScrapeStep("Shopify storefront detected"));

                var shopify = await parsers[0].TryParseAsync(new ScrapeContext(url, null, http), cancellationToken);

                if (shopify is not null)
                {
                    progress?.Report(new ScrapeStep("Product data read", clock.Elapsed));
                    merged.Absorb(shopify, parsers[0].Name);
                }
            }

            // In practice this always runs after Shopify: the .js payload carries no
            // currency field, and guessing one would break the rule that a price is stored
            // in whatever currency the shop quoted. The page is worth the bytes for that
            // alone. The check stays because a strategy that does fill every field should
            // not trigger a download.
            if (!merged.IsComplete)
            {
                var html = await FetchAsync(url, progress, clock, cancellationToken);

                if (html is null)
                {
                    failure = $"That page could not be read. {FallThrough}";
                }
                else
                {
                    progress?.Report(new ScrapeStep("Reading product data"));

                    using var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
                    var context = new ScrapeContext(url, document, http);

                    foreach (var parser in parsers.Skip(1))
                    {
                        if (merged.IsComplete)
                        {
                            break;
                        }

                        if (parser is JsonLdParser)
                        {
                            progress?.Report(new ScrapeStep("Reading price"));
                        }

                        var info = await parser.TryParseAsync(context, cancellationToken);

                        if (info is not null)
                        {
                            merged.Absorb(info, parser.Name);
                        }
                    }
                }
            }
        }
        catch (HttpRequestException exception)
        {
            failure = Explain(exception.StatusCode);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation.
            failure = $"The shop took too long to answer. {FallThrough}";
        }

        // A failure only refuses when it left us with nothing. Margaux answers the .js
        // endpoint and then 404s the page it came from; throwing away a good Shopify read
        // because the follow-up request failed would be losing what we already have.
        if (merged.Nothing)
        {
            return Refused(normalised, failure ?? $"This shop hides its details from readers. {FallThrough}");
        }

        return merged.ToOutcome(url);
    }

    /// <summary>Returns null for a page that answered but is not readable markup.</summary>
    private async Task<string?> FetchAsync(
        Uri url,
        IProgress<ScrapeStep>? progress,
        Stopwatch clock,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("The shop refused the request.", null, response.StatusCode);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        progress?.Report(new ScrapeStep("Page retrieved", clock.Elapsed));

        return html;
    }

    private static string Explain(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
            $"This shop hides its details from readers. {FallThrough}",
        HttpStatusCode.TooManyRequests =>
            $"The shop asked us to slow down. {FallThrough}",
        HttpStatusCode.NotFound =>
            $"That page is not there any more. {FallThrough}",
        _ =>
            $"That page could not be reached. {FallThrough}",
    };

    private static ScrapeOutcome Refused(string url, string message) =>
        new(new ProductInfo(url, null, null, null, null, null, []),
            new Dictionary<ProductField, string>(),
            message);

    /// <summary>
    /// Field by field, first non-null wins — so Shopify's price survives even when a later
    /// strategy also has one, and og:title fills a gap Shopify left.
    /// </summary>
    private sealed class Merge(string url)
    {
        private readonly Dictionary<ProductField, string> sources = [];
        private readonly List<string> readFrom = [];

        private string? title;
        private string? brand;
        private string? imageUrl;
        private decimal? price;
        private string? currency;

        public bool IsComplete => title is not null && price is not null && currency is not null && imageUrl is not null;

        public bool Nothing => sources.Count == 0;

        public void Absorb(ProductInfo info, string source)
        {
            var contributed = false;

            contributed |= Take(ProductField.Title, info.Title, ref title, source);
            contributed |= Take(ProductField.Brand, info.Brand, ref brand, source);
            contributed |= Take(ProductField.ImageUrl, info.ImageUrl, ref imageUrl, source);
            contributed |= Take(ProductField.Currency, info.Currency, ref currency, source);

            if (price is null && info.Price is not null)
            {
                price = info.Price;
                sources[ProductField.Price] = source;
                contributed = true;
            }

            if (contributed)
            {
                readFrom.Add(source);
            }
        }

        public ScrapeOutcome ToOutcome(Uri pageUrl)
        {
            // Relative image URLs are resolved centrally, so no parser has to think about it.
            if (imageUrl is not null && Uri.TryCreate(pageUrl, imageUrl, out var absolute))
            {
                imageUrl = absolute.ToString();
            }

            return new ScrapeOutcome(
                new ProductInfo(url, title, brand, imageUrl, price, currency?.ToUpperInvariant(), readFrom),
                sources,
                null);
        }

        private bool Take(ProductField field, string? candidate, ref string? slot, string source)
        {
            if (slot is not null || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            slot = candidate.Trim();
            sources[field] = source;

            return true;
        }
    }
}
