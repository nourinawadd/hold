using System.Diagnostics;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Hold.Scraping;

/// <summary>
/// Runs the strategy chain and merges what comes back. Never throws for a page it could not
/// read: the add flow must always fall through to manual entry, so every failure returns a
/// sentence instead.
/// </summary>
public sealed class ProductScraper
{
    // The fall-through line from the spec. Every refusal ends the same way, because the
    // user's next move is the same in every case.
    private const string FallThrough = "Fill in what you know — the link is saved.";

    private readonly HttpClient http;
    private readonly PageRecovery recovery;
    private readonly IProductParser[] parsers;

    /// <summary>
    /// <paramref name="extractor"/> is optional: without one the chain skips the model and
    /// runs on structured data and the URL alone.
    /// </summary>
    public ProductScraper(HttpClient http, IProductExtractor? extractor = null)
    {
        this.http = http;
        this.recovery = new PageRecovery(http);

        extractor ??= NoExtractor.Instance;

        // Ordered by how much each answer can be trusted: structured data, then prose,
        // then the link itself. Every one below Shopify needs a page except the last,
        // which is why the last is the one that survives a shop that blocks readers.
        List<IProductParser> chain =
        [
            new ShopifyParser(),
            new JsonLdParser(),
            new OpenGraphParser(),
            new MicrodataParser(),
        ];

        if (extractor.Enabled)
        {
            chain.Add(new LlmParser(extractor));
        }

        chain.Add(new UrlSlugParser());

        this.parsers = [.. chain];
    }

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
        var context = new ScrapeContext(url, null, http);
        string? failure = null;
        var archived = false;

        // Declared out here so it outlives the fetch block: the parser loop below reads it,
        // and a document disposed at the end of an inner scope is an empty document.
        IDocument? document = null;

        try
        {
            // 1 — Shopify, which answers in about 8KB where the rendered page costs a
            // megabyte. Worth trying before anything is downloaded.
            if (ShopifyParser.Matches(url))
            {
                progress?.Report(new ScrapeStep("Shopify storefront detected"));

                try
                {
                    var shopify = await parsers[0].TryParseAsync(context, cancellationToken);

                    if (shopify is not null)
                    {
                        progress?.Report(new ScrapeStep("Product data read", clock.Elapsed));
                        merged.Absorb(shopify, parsers[0].Name);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    // A dead host here must not end the attempt. Every later rung, including
                    // the one that reads the link itself, still deserves its turn.
                    failure = exception is HttpRequestException http
                        ? Explain(http.StatusCode)
                        : $"The shop took too long to answer. {FallThrough}";
                }
            }

            // In practice this always runs after Shopify: the .js payload carries no
            // currency field, and guessing one would break the rule that a price is stored
            // in whatever currency the shop quoted. The page is worth the bytes for that
            // alone. The check stays because a strategy that does fill every field should
            // not trigger a download.
            if (!merged.IsComplete)
            {
                string? html = null;

                try
                {
                    html = await FetchAsync(url, progress, clock, cancellationToken);
                }
                catch (HttpRequestException exception)
                {
                    // Remembered, not thrown: a refusal here is the reason to start the
                    // recovery ladder, not the end of the attempt.
                    failure = Explain(exception.StatusCode);
                }

                // Ladder, in order of how much the answer can be trusted. Each rung only
                // runs because the one above it produced nothing.
                if (html is null)
                {
                    var recovered = await recovery.TryVariantsAsync(url, progress, cancellationToken)
                        ?? await recovery.TryArchiveAsync(url, progress, cancellationToken);

                    if (recovered is not null)
                    {
                        progress?.Report(new ScrapeStep($"Recovered from {recovered.Via}"));

                        html = recovered.Html;

                        // An archived price is a price from some other month. Money is the
                        // one field that must never come from a copy.
                        archived = recovered.Via.Contains("archived", StringComparison.Ordinal);
                        failure = null;
                    }
                }

                if (html is null)
                {
                    failure ??= $"That page could not be read. {FallThrough}";
                }
                else
                {
                    progress?.Report(new ScrapeStep("Reading product data"));

                    document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);
                    context = new ScrapeContext(url, document, http);
                }
            }

            // Every remaining strategy, including the ones that need no page at all. The
            // URL parser runs even when nothing was fetched — that is the whole point of it.
            foreach (var parser in parsers.Skip(1))
            {
                if (merged.IsComplete)
                {
                    break;
                }

                if (context.Document is null && parser is not UrlSlugParser)
                {
                    continue;
                }

                Announce(parser, progress);

                var info = await parser.TryParseAsync(context, cancellationToken);

                if (info is null)
                {
                    continue;
                }

                merged.Absorb(archived ? info with { Price = null, Currency = null } : info, parser.Name);
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
        finally
        {
            document?.Dispose();
        }

        // A failure only refuses when it left us with nothing. Margaux answers the .js
        // endpoint and then 404s the page it came from; throwing away a good Shopify read
        // because the follow-up request failed would be losing what we already have.
        if (merged.Nothing)
        {
            return Refused(normalised, failure ?? $"This shop hides its details from readers. {FallThrough}");
        }

        // Partial, not refused: the shop blocked us but the link still named the product.
        // The sentence stays so the user knows why the fields are thin and unverified —
        // this is the spec's fourth state, reached with the form already part-filled. It is
        // dropped when a strong source answered, because then nothing needs excusing.
        return merged.ToOutcome(url) with
        {
            Message = merged.HasStrongSource ? null : failure,
        };
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

        // A bot-manager interstitial answers 200 with a page of challenge script. Reporting
        // that as the page retrieved would be a false account of what happened, and parsing
        // it wastes the rest of the ladder.
        if (PageRecovery.IsChallenge(html))
        {
            progress?.Report(new ScrapeStep("Shop answered with a checkpoint, not the page", clock.Elapsed));

            return null;
        }

        progress?.Report(new ScrapeStep("Page retrieved", clock.Elapsed));

        return html;
    }

    /// <summary>Names the step so the reading state stays a true account of what ran.</summary>
    private static void Announce(IProductParser parser, IProgress<ScrapeStep>? progress)
    {
        var step = parser switch
        {
            JsonLdParser => "Reading price",
            // Named outright: the user should know when a model is reading the page rather
            // than a parser, because those values are guesses.
            LlmParser => "No structured data — asking Claude",
            UrlSlugParser => "Reading the link itself",
            _ => null,
        };

        if (step is not null)
        {
            progress?.Report(new ScrapeStep(step));
        }
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

        /// <summary>
        /// True when a strategy that reads published data contributed. If one did, a failure
        /// further down the ladder is noise — Margaux answers its .js endpoint and then 404s
        /// the page, and telling the user "that page is not there any more" over a correct
        /// product would be alarming and wrong.
        /// </summary>
        public bool HasStrongSource =>
            readFrom.Any(name => name is ScrapeOutcome.ShopifyName or ScrapeOutcome.JsonLdName);

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
