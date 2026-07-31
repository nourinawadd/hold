using System.Diagnostics;
using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hold.Scraping;

public sealed class ProductScraper
{
    private const string FallThrough = "Fill in what you know — the link is saved.";

    private readonly HttpClient http;
    private readonly ILogger<ProductScraper> log;
    private readonly PageRecovery recovery;
    private readonly IProductParser[] parsers;

    public ProductScraper(
        HttpClient http,
        IProductExtractor? extractor = null,
        ILogger<ProductScraper>? logger = null)
    {
        this.http = http;
        this.log = logger ?? NullLogger<ProductScraper>.Instance;
        this.recovery = new PageRecovery(http);

        extractor ??= NoExtractor.Instance;

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

        IDocument? document = null;

        try
        {
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
                    failure = exception is HttpRequestException http
                        ? Explain(http.StatusCode)
                        : $"The shop took too long to answer. {FallThrough}";

                    log.LogInformation(
                        exception,
                        "Shopify endpoint failed for {Host}; continuing down the ladder.",
                        url.Host);
                }
            }

            if (!merged.IsComplete)
            {
                string? html = null;

                try
                {
                    html = await FetchAsync(url, progress, clock, cancellationToken);
                }
                catch (HttpRequestException exception)
                {
                    failure = Explain(exception.StatusCode);

                    log.LogWarning(
                        "{Host} refused the page with {Status}.",
                        url.Host,
                        exception.StatusCode);
                }

                if (html is null)
                {
                    var recovered = await recovery.TryVariantsAsync(url, progress, cancellationToken)
                        ?? await recovery.TryArchiveAsync(url, progress, cancellationToken);

                    if (recovered is not null)
                    {
                        progress?.Report(new ScrapeStep($"Recovered from {recovered.Via}"));

                        log.LogInformation(
                            "Recovered {Host} from {Via}.", url.Host, recovered.Via);

                        html = recovered.Html;

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
            failure = $"The shop took too long to answer. {FallThrough}";

            log.LogWarning("{Host} did not answer within the timeout.", url.Host);
        }
        finally
        {
            document?.Dispose();
        }

        log.LogInformation(
            "Read {Host}: {Fields} from {Sources}.{Outcome}",
            url.Host,
            merged.Nothing ? "nothing" : string.Join("+", merged.FieldNames()),
            merged.Nothing ? "no source" : string.Join("+", merged.SourceNames()),
            failure is null ? string.Empty : $" Fell through: {failure}");

        if (merged.Nothing)
        {
            return Refused(normalised, failure ?? $"This shop hides its details from readers. {FallThrough}");
        }

        return merged.ToOutcome(url) with
        {
            Message = merged.HasStrongSource ? null : failure,
        };
    }

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

        if (PageRecovery.IsChallenge(html))
        {
            progress?.Report(new ScrapeStep("Shop answered with a checkpoint, not the page", clock.Elapsed));

            log.LogWarning(
                "{Host} answered 200 with a bot-challenge page ({Bytes} bytes), not the product.",
                url.Host,
                html.Length);

            return null;
        }

        progress?.Report(new ScrapeStep("Page retrieved", clock.Elapsed));

        return html;
    }

    private static void Announce(IProductParser parser, IProgress<ScrapeStep>? progress)
    {
        var step = parser switch
        {
            JsonLdParser => "Reading price",
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

        public IEnumerable<string> FieldNames() => sources.Keys.Select(field => field.ToString());

        public IEnumerable<string> SourceNames() => readFrom;

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
