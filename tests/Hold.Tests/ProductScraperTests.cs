namespace Hold.Tests;

/// <summary>
/// The rule these all defend: a shop that refuses must produce a sentence, never an
/// exception. The add flow falls through to manual entry in every case, so a throw here
/// would take the whole panel down.
/// </summary>
public class ProductScraperTests
{
    private const string ProductUrl = "https://example.com/products/a-thing";

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task RefusalBecomesAMessage(HttpStatusCode status)
    {
        using var http = StubHandler.Returning(status);

        var outcome = await new ProductScraper(http).ReadAsync(ProductUrl);

        Assert.NotNull(outcome.Message);

        // Every refusal ends with the same instruction, because the next move is the same.
        Assert.Contains("Fill in what you know", outcome.Message);
        Assert.Equal(ProductUrl, outcome.Info.Url);

        // A refusal is no longer empty-handed: the link itself still names the product, so
        // the panel opens part-filled with a faint title rather than a blank form. The
        // sentence stays to explain why the fields are thin.
        Assert.Equal("A Thing", outcome.Info.Title);
        Assert.Equal([ScrapeOutcome.UrlName], outcome.Info.ReadFrom);
        Assert.True(outcome.IsUnverified(ProductField.Title));

        // Still nothing invented about money.
        Assert.Null(outcome.Info.Price);
        Assert.Null(outcome.Info.Currency);
    }

    [Fact]
    public async Task ForbiddenSaysTheShopHidesItsDetails()
    {
        using var http = StubHandler.Returning(HttpStatusCode.Forbidden);

        var outcome = await new ProductScraper(http).ReadAsync(ProductUrl);

        Assert.Contains("hides its details", outcome.Message);
    }

    [Fact]
    public async Task TimeoutBecomesAMessage()
    {
        using var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(150) };

        var outcome = await new ProductScraper(http).ReadAsync(ProductUrl);

        Assert.NotNull(outcome.Message);
        Assert.Contains("too long", outcome.Message);
    }

    [Fact]
    public async Task ConnectionFailureBecomesAMessage()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("no such host")));

        var outcome = await new ProductScraper(http).ReadAsync(ProductUrl);

        Assert.NotNull(outcome.Message);
        Assert.Contains("Fill in what you know", outcome.Message);
    }

    [Fact]
    public async Task SomethingThatIsNotALinkIsRefusedWithoutAnyRequest()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new UnreachableException("must not be called")));

        var outcome = await new ProductScraper(http).ReadAsync("just some words");

        Assert.Contains("does not look like a web link", outcome.Message);
    }

    [Fact]
    public async Task ReadsTheShopifyPathAndKeepsItsValues()
    {
        var pageRequested = false;

        using var http = new HttpClient(new StubHandler(request =>
        {
            var isJsEndpoint = request.RequestUri!.AbsolutePath.EndsWith(".js", StringComparison.Ordinal);

            if (!isJsEndpoint)
            {
                pageRequested = true;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = isJsEndpoint
                    ? new StringContent(Fixture.Text(Fixture.ShopifyDoen), Encoding.UTF8, "application/json")
                    : new StringContent("<html><head></head><body></body></html>", Encoding.UTF8, "text/html"),
            };
        }));

        var outcome = await new ProductScraper(http)
            .ReadAsync("https://shopdoen.com/products/long-scoop-neck-slip-black-2");

        Assert.Null(outcome.Message);
        Assert.Equal("LONG SCOOP NECK SLIP -- BLACK", outcome.Info.Title);
        Assert.Equal("DOEN", outcome.Info.Brand);
        Assert.Equal(68.00m, outcome.Info.Price);
        Assert.Equal(ScrapeOutcome.ShopifyName, outcome.Sources[ProductField.Title]);

        // Shopify's .js payload has no currency field, so the page is still fetched to look
        // for one. Completeness is judged on all four fields, not just the ones it filled.
        Assert.True(pageRequested, "the page is still fetched when Shopify leaves a field empty");
    }

    [Fact]
    public async Task KeepsWhatShopifyGaveWhenThePageFetchThenFails()
    {
        // Found live on margauxny.com: the .js endpoint answers, and the page it came from
        // returns 404. Losing a good read because the follow-up request failed would be
        // throwing away data already in hand.
        using var http = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".js", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Fixture.Text(Fixture.ShopifyDoen), Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        var outcome = await new ProductScraper(http)
            .ReadAsync("https://shopdoen.com/products/long-scoop-neck-slip-black-2");

        Assert.Null(outcome.Message);
        Assert.Equal("LONG SCOOP NECK SLIP -- BLACK", outcome.Info.Title);
        Assert.Equal(68.00m, outcome.Info.Price);
    }

    [Fact]
    public async Task StillRefusesWhenTheFailureLeftNothing()
    {
        // Same shape, but nothing was gathered first, so the refusal stands.
        using var http = StubHandler.Returning(HttpStatusCode.NotFound);

        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/thing");

        Assert.NotNull(outcome.Message);
        Assert.Contains("not there any more", outcome.Message);
    }

    [Fact]
    public async Task StopsAskingOnceEveryFieldIsFilled()
    {
        // Complete JSON-LD, with microdata below it disagreeing about everything. The
        // chain should stop at JSON-LD and never consult the weaker source.
        const string html = """
            <html><head>
              <script type="application/ld+json">
                { "@context":"https://schema.org", "@type":"Product",
                  "name":"From JSON-LD", "image":"https://cdn.example/a.jpg",
                  "brand":"Real Brand",
                  "offers":{ "@type":"Offer", "price":"12.00", "priceCurrency":"EUR" } }
              </script>
            </head>
            <body>
              <div itemscope itemtype="https://schema.org/Product">
                <span itemprop="name">From Microdata</span>
                <span itemprop="price">999.00</span>
                <meta itemprop="priceCurrency" content="JPY" />
              </div>
            </body></html>
            """;

        using var http = StubHandler.Serving(html, "text/html");

        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/thing");

        Assert.Equal("From JSON-LD", outcome.Info.Title);
        Assert.Equal(12.00m, outcome.Info.Price);
        Assert.Equal("EUR", outcome.Info.Currency);

        // Microdata never contributed, so it is not named as a source.
        Assert.DoesNotContain(ScrapeOutcome.MicrodataName, outcome.Info.ReadFrom);
    }

    [Fact]
    public async Task NormalisesTheUrlBeforeSavingIt()
    {
        using var http = StubHandler.Returning(HttpStatusCode.Forbidden);

        var outcome = await new ProductScraper(http)
            .ReadAsync("https://example.com/products/a-thing?utm_source=instagram&gclid=xyz#reviews");

        Assert.Equal(ProductUrl, outcome.Info.Url);
    }

    [Fact]
    public async Task ReportsItsStepsAsItGoes()
    {
        using var http = StubHandler.Serving(
            """<html><head><meta property="og:title" content="A Thing" /></head><body></body></html>""",
            "text/html");

        var collector = new StepCollector();

        await new ProductScraper(http).ReadAsync(ProductUrl, collector);

        var texts = collector.Steps.Select(step => step.Text).ToList();

        // The first line names the shop, which is what the reading state uses as its
        // heading. The rest are the chain reporting itself.
        Assert.Equal("Reading example.com", texts[0]);
        Assert.Contains("Shopify storefront detected", texts);
        Assert.Contains("Reading product data", texts);

        // At least one step carries a real elapsed time — these are measurements, not
        // decoration.
        Assert.Contains(collector.Steps, step => step.Elapsed is not null);
    }

    [Fact]
    public async Task APageWithNothingReadableStillFallsThrough()
    {
        using var http = StubHandler.Serving("<html><head></head><body>hello</body></html>", "text/html");

        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/thing");

        Assert.NotNull(outcome.Message);
        Assert.Contains("Fill in what you know", outcome.Message);
    }

    [Fact]
    public async Task MergesAcrossStrategiesAndRecordsWhichSuppliedWhat()
    {
        // JSON-LD has the name but no price; OpenGraph carries the price. The merge should
        // take each from where it exists, and remember where that was.
        const string html = """
            <html><head>
              <meta property="og:title" content="An OG Title" />
              <meta property="product:price:amount" content="42.50" />
              <meta property="product:price:currency" content="GBP" />
              <script type="application/ld+json">
                { "@context":"https://schema.org", "@type":"Product", "name":"The Real Name" }
              </script>
            </head><body></body></html>
            """;

        using var http = StubHandler.Serving(html, "text/html");

        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/thing");

        Assert.Equal("The Real Name", outcome.Info.Title);
        Assert.Equal(42.50m, outcome.Info.Price);
        Assert.Equal("GBP", outcome.Info.Currency);

        Assert.Equal(ScrapeOutcome.JsonLdName, outcome.Sources[ProductField.Title]);
        Assert.Equal(ScrapeOutcome.OpenGraphName, outcome.Sources[ProductField.Price]);

        // Which is what drives the faint rendering.
        Assert.False(outcome.IsUnverified(ProductField.Title));
        Assert.True(outcome.IsUnverified(ProductField.Price));
    }

    [Fact]
    public async Task ResolvesRelativeImagesAgainstThePage()
    {
        const string html = """
            <html><head>
              <script type="application/ld+json">
                { "@context":"https://schema.org", "@type":"Product",
                  "name":"Thing", "image":"/media/thing.jpg" }
              </script>
            </head><body></body></html>
            """;

        using var http = StubHandler.Serving(html, "text/html");

        var outcome = await new ProductScraper(http).ReadAsync("https://example.com/shop/thing");

        Assert.Equal("https://example.com/media/thing.jpg", outcome.Info.ImageUrl);
    }
}
