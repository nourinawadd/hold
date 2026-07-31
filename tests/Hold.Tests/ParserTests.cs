namespace Hold.Tests;

public class ShopifyParserTests
{
    private static readonly Uri ProductUrl =
        new("https://shopdoen.com/products/long-scoop-neck-slip-black-2");

    [Fact]
    public async Task ReadsTheSavedDoenProduct()
    {
        using var http = StubHandler.Serving(Fixture.Text(Fixture.ShopifyDoen), "application/json");

        var info = await new ShopifyParser().TryParseAsync(new ScrapeContext(ProductUrl, null, http), default);

        Assert.NotNull(info);
        Assert.Equal("LONG SCOOP NECK SLIP -- BLACK", info.Title);
        Assert.Equal("DOEN", info.Brand);

        Assert.Equal(68.00m, info.Price);

        Assert.NotNull(info.ImageUrl);
        Assert.StartsWith("https://", info.ImageUrl);
    }

    [Theory]
    [InlineData("https://shopdoen.com/products/some-handle", true)]
    [InlineData("https://shop.example.com/collections/all/products/a-thing", true)]
    [InlineData("https://shopdoen.com/collections/dresses", false)]
    [InlineData("https://shopdoen.com/products/", false)]
    [InlineData("https://example.com/", false)]
    public void RecognisesOnlyProductPaths(string url, bool expected) =>
        Assert.Equal(expected, ShopifyParser.Matches(new Uri(url)));

    [Fact]
    public async Task IgnoresAStorefrontThatAnswersWithHtml()
    {
        using var http = StubHandler.Serving("<html><body>not json</body></html>", "text/html");

        Assert.Null(await new ShopifyParser().TryParseAsync(new ScrapeContext(ProductUrl, null, http), default));
    }

    [Fact]
    public async Task IgnoresAFailedRequest()
    {
        using var http = StubHandler.Returning(HttpStatusCode.NotFound);

        Assert.Null(await new ShopifyParser().TryParseAsync(new ScrapeContext(ProductUrl, null, http), default));
    }
}

public class JsonLdParserTests
{
    private static readonly Uri PageUrl = new("https://naadam.co/products/womens-snoopy-hug-cashmere-sweater");

    [Fact]
    public async Task ReadsAProductGroupFromTheSavedNaadamPage()
    {
        using var document = await Fixture.DocumentAsync(Fixture.JsonLdNaadam);
        using var http = new HttpClient();

        var info = await new JsonLdParser().TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.NotNull(info);
        Assert.Equal("Women's Snoopy Hug Cashmere Sweater", info.Title);
        Assert.Equal("Naadam", info.Brand);

        Assert.Equal("USD", info.Currency);
        Assert.NotNull(info.Price);
        Assert.NotNull(info.ImageUrl);
    }

    [Fact]
    public async Task TakesTheGroupNameNotTheVariantName()
    {
        using var document = await Fixture.DocumentAsync(Fixture.JsonLdNaadam);
        using var http = new HttpClient();

        var info = await new JsonLdParser().TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.DoesNotContain("XXS", info!.Title);
    }

    [Fact]
    public async Task SurvivesAMalformedBlockAndReadsTheGraph()
    {
        using var document = await Fixture.DocumentAsync(Fixture.JsonLdEdge);
        using var http = new HttpClient();

        var info = await new JsonLdParser()
            .TryParseAsync(new ScrapeContext(new Uri("https://example.com/p/1"), document, http), default);

        Assert.NotNull(info);

        Assert.Equal("Wool Overshirt", info.Title);

        Assert.Equal("Atelier Nord", info.Brand);
        Assert.Equal("https://cdn.ateliernord.example/overshirt-1.jpg", info.ImageUrl);

        Assert.Equal(1234.56m, info.Price);
        Assert.Equal("EUR", info.Currency);
    }

    [Fact]
    public async Task ReturnsNothingWhenThePageHasNoStructuredData()
    {
        using var document = await Fixture.DocumentAsync(Fixture.Microdata);
        using var http = new HttpClient();

        Assert.Null(await new JsonLdParser()
            .TryParseAsync(new ScrapeContext(new Uri("https://example.com/p/1"), document, http), default));
    }
}

public class OpenGraphParserTests
{
    private static readonly Uri PageUrl = new("https://hedleyandbennett.com/products/the-chic-cook-set");

    [Fact]
    public async Task ReadsTheSavedHedleyPage()
    {
        using var document = await Fixture.DocumentAsync(Fixture.OpenGraphHedley);
        using var http = new HttpClient();

        var info = await new OpenGraphParser().TryParseAsync(new ScrapeContext(PageUrl, document, http), default);

        Assert.NotNull(info);
        Assert.Contains("Chic Cook Set", info.Title);

        Assert.Equal(355.00m, info.Price);
        Assert.Equal("USD", info.Currency);

        Assert.Equal("Hedley & Bennett", info.Brand);
        Assert.NotNull(info.ImageUrl);
    }

    [Fact]
    public void IsNotTrustedInFullInk() =>
        Assert.False(new OpenGraphParser().StrongSource);

    [Fact]
    public async Task FallsBackToTheTitleElement()
    {
        using var document = await Fixture.ParseAsync(
            "<html><head><title>  A Plain Page  </title></head><body></body></html>");
        using var http = new HttpClient();

        var info = await new OpenGraphParser()
            .TryParseAsync(new ScrapeContext(new Uri("https://example.com/p/1"), document, http), default);

        Assert.Equal("A Plain Page", info?.Title);
    }
}

public class MicrodataParserTests
{
    [Fact]
    public async Task ReadsTheHandAuthoredPage()
    {
        using var document = await Fixture.DocumentAsync(Fixture.Microdata);
        using var http = new HttpClient();

        var info = await new MicrodataParser()
            .TryParseAsync(new ScrapeContext(new Uri("https://corvid.example/lamp"), document, http), default);

        Assert.NotNull(info);
        Assert.Equal("Brass Reading Lamp", info.Title);

        Assert.Equal("Corvid Supply", info.Brand);

        Assert.Equal(1248.00m, info.Price);
        Assert.Equal("GBP", info.Currency);
    }

    [Fact]
    public async Task ReturnsNothingWhenThereIsNoMicrodata()
    {
        using var document = await Fixture.DocumentAsync(Fixture.JsonLdEdge);
        using var http = new HttpClient();

        Assert.Null(await new MicrodataParser()
            .TryParseAsync(new ScrapeContext(new Uri("https://example.com/p/1"), document, http), default));
    }
}
