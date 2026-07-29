namespace Hold.Tests;

/// <summary>Records what it was asked and returns what it was told to.</summary>
internal sealed class StubExtractor(ProductDraft? draft, bool enabled = true) : IProductExtractor
{
    public bool Enabled { get; } = enabled;

    public int Calls { get; private set; }

    public string? LastPageText { get; private set; }

    public Task<ProductDraft?> ExtractAsync(string pageText, Uri url, CancellationToken cancellationToken)
    {
        Calls++;
        LastPageText = pageText;

        return Task.FromResult(draft);
    }
}

public class LlmParserTests
{
    private static readonly Uri PageUrl = new("https://corvid.example/lamp");

    /// <summary>A shop with no structured data at all — the case this exists for.</summary>
    private const string BareShopHtml = """
        <html><head>
          <title>Brass Reading Lamp — Corvid Supply</title>
          <meta name="description" content="A weighted brass lamp." />
          <script>var analytics = {price: 99999, name: "TRACKING NOISE"};</script>
          <style>.price { color: red; }</style>
        </head><body>
          <h1>Brass Reading Lamp</h1>
          <p>by Corvid Supply</p>
          <span>£1,248.00</span>
        </body></html>
        """;

    [Fact]
    public async Task ReadsWhatTheModelReported()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(
            new ProductDraft("Brass Reading Lamp", "Corvid Supply", "£1,248.00", "GBP"));

        var info = await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.NotNull(info);
        Assert.Equal("Brass Reading Lamp", info.Title);
        Assert.Equal("Corvid Supply", info.Brand);

        // The model returns the price as printed; PriceNormaliser does the parsing, so the
        // European/US separator logic is not re-implemented in a prompt.
        Assert.Equal(1248.00m, info.Price);
        Assert.Equal("GBP", info.Currency);
    }

    [Fact]
    public async Task FallsBackToTheCurrencySymbolWhenTheModelDidNotName_One()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft("Lamp", null, "£1,248.00", null));

        var info = await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.Equal("GBP", info!.Currency);
    }

    [Fact]
    public void IsNeverTrustedInFullInk() =>
        // Everything it supplies renders faint and waits to be confirmed.
        Assert.False(new LlmParser(new StubExtractor(null)).StrongSource);

    [Fact]
    public async Task NeverSuppliesAnImage()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft("Lamp", null, "10.00", "USD"));

        var info = await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        // Picking an image out of prose is a guess with no way to check it.
        Assert.Null(info!.ImageUrl);
    }

    [Fact]
    public async Task StripsScriptAndStyleFromWhatItSends()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft("Lamp", null, null, null));

        await new LlmParser(extractor).TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        var sent = extractor.LastPageText!;

        // Analytics blobs invent prices. They must not reach the model.
        Assert.DoesNotContain("TRACKING NOISE", sent);
        Assert.DoesNotContain("99999", sent);
        Assert.DoesNotContain("color: red", sent);

        // The visible text and the head metadata do.
        Assert.Contains("Brass Reading Lamp", sent);
        Assert.Contains("£1,248.00", sent);
        Assert.Contains("A weighted brass lamp.", sent);
    }

    [Fact]
    public async Task CapsWhatItSends()
    {
        var padding = string.Join(" ", Enumerable.Repeat("filler", 20_000));
        using var document = await Fixture.ParseAsync($"<html><body><p>{padding}</p></body></html>");
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft("x", null, null, null));

        await new LlmParser(extractor).TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        // A megabyte page must not become a megabyte prompt.
        Assert.True(extractor.LastPageText!.Length <= 12_000, extractor.LastPageText.Length.ToString());
    }

    [Fact]
    public async Task DoesNothingWhenDisabled()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(
            new ProductDraft("Lamp", null, null, null), enabled: false);

        var info = await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.Null(info);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task ReturnsNothingWhenTheModelFoundNothing()
    {
        using var document = await Fixture.ParseAsync(BareShopHtml);
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft(null, null, null, null));

        Assert.Null(await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, document, http), default));
    }

    [Fact]
    public async Task IsSkippedEntirelyWhenThePageWasNeverFetched()
    {
        using var http = new HttpClient();

        var extractor = new StubExtractor(new ProductDraft("Lamp", null, null, null));

        // A 403 leaves no document. There is nothing for a model to read, so it is not
        // asked — this is the case the whole strategy cannot help with.
        Assert.Null(await new LlmParser(extractor)
            .TryParseAsync(new ScrapeContext(PageUrl, null, http), default));
        Assert.Equal(0, extractor.Calls);
    }
}

public class LlmChainTests
{
    [Fact]
    public async Task TheModelIsOnlyAskedWhenTheStructuredParsersCameUpEmpty()
    {
        // Complete JSON-LD: the chain is satisfied before it reaches the model.
        const string html = """
            <html><head>
              <script type="application/ld+json">
                { "@context":"https://schema.org", "@type":"Product", "name":"Structured",
                  "image":"https://cdn.example/a.jpg", "brand":"Real",
                  "offers":{ "@type":"Offer", "price":"12.00", "priceCurrency":"EUR" } }
              </script>
            </head><body></body></html>
            """;

        using var http = StubHandler.Serving(html, "text/html");
        var extractor = new StubExtractor(new ProductDraft("Should Not Be Used", null, null, null));

        var outcome = await new ProductScraper(http, extractor).ReadAsync("https://example.com/thing");

        Assert.Equal("Structured", outcome.Info.Title);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task TheModelRescuesAPageWithNoStructuredData()
    {
        const string html = "<html><head></head><body><h1>A Thing</h1><p>$42.50</p></body></html>";

        using var http = StubHandler.Serving(html, "text/html");
        var extractor = new StubExtractor(new ProductDraft("A Thing", "The Shop", "$42.50", "USD"));

        var outcome = await new ProductScraper(http, extractor).ReadAsync("https://example.com/thing");

        Assert.Null(outcome.Message);
        Assert.Equal("A Thing", outcome.Info.Title);
        Assert.Equal(42.50m, outcome.Info.Price);
        Assert.Equal(1, extractor.Calls);

        // Provenance names it, and the value renders faint.
        Assert.Equal(ScrapeOutcome.LlmName, outcome.Sources[ProductField.Price]);
        Assert.True(outcome.IsUnverified(ProductField.Price));
        Assert.Contains("Claude", outcome.Provenance());
    }

    [Fact]
    public async Task TheTitleElementStillWinsOverTheModel()
    {
        // Documenting a consequence of first-non-null-wins, not endorsing it: OpenGraph's
        // <title> fallback runs before the model, so a page titled with the shop's name
        // yields "Shop" rather than the product the model identified. Both are weak
        // sources and both render faint, so the user corrects one field either way — but
        // the model's price still fills the gap that mattered.
        const string html = "<html><head><title>Shop</title></head><body><p>$42.50</p></body></html>";

        using var http = StubHandler.Serving(html, "text/html");
        var extractor = new StubExtractor(new ProductDraft("A Thing", null, "$42.50", "USD"));

        var outcome = await new ProductScraper(http, extractor).ReadAsync("https://example.com/thing");

        Assert.Equal("Shop", outcome.Info.Title);
        Assert.Equal(ScrapeOutcome.OpenGraphName, outcome.Sources[ProductField.Title]);
        Assert.Equal(42.50m, outcome.Info.Price);
        Assert.Equal(ScrapeOutcome.LlmName, outcome.Sources[ProductField.Price]);
    }

    [Fact]
    public async Task AStrongSourceStillWinsFieldByField()
    {
        // og:title is present but there is no price. The model fills only the gap.
        const string html = """
            <html><head>
              <meta property="og:title" content="From OpenGraph" />
            </head><body><p>$42.50</p></body></html>
            """;

        using var http = StubHandler.Serving(html, "text/html");
        var extractor = new StubExtractor(new ProductDraft("From Claude", null, "$42.50", "USD"));

        var outcome = await new ProductScraper(http, extractor).ReadAsync("https://example.com/thing");

        Assert.Equal("From OpenGraph", outcome.Info.Title);
        Assert.Equal(ScrapeOutcome.OpenGraphName, outcome.Sources[ProductField.Title]);
        Assert.Equal(ScrapeOutcome.LlmName, outcome.Sources[ProductField.Price]);
    }

    [Fact]
    public async Task ARefusedShopNeverReachesTheModel()
    {
        using var http = StubHandler.Returning(HttpStatusCode.Forbidden);
        var extractor = new StubExtractor(new ProductDraft("Never", null, null, null));

        var outcome = await new ProductScraper(http, extractor).ReadAsync("https://example.com/products/x");

        // 403 means no HTML. A model cannot read a page nobody received.
        Assert.Contains("hides its details", outcome.Message);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task WithoutAKeyTheChainIsUnchanged()
    {
        const string html = "<html><head><title>Shop</title></head><body><h1>A Thing</h1></body></html>";

        using var http = StubHandler.Serving(html, "text/html");

        // No extractor argument at all — the four-strategy chain from the spec.
        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/thing");

        Assert.DoesNotContain(ScrapeOutcome.LlmName, outcome.Info.ReadFrom);
    }
}
