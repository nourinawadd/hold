namespace Hold.Tests;

public class UrlSlugParserTests
{
    [Theory]
    [InlineData(
        "https://www.pullandbear.com/eg/en/oversize-sun-graphic-tshirt-l07230312?cS=300&pelement=748863385",
        "Oversize Sun Graphic Tshirt")]
    [InlineData("https://www.zara.com/us/en/ribbed-tank-top-p04174168.html", "Ribbed Tank Top")]
    [InlineData("https://shopdoen.com/products/long-scoop-neck-slip-black-2", "Long Scoop Neck Slip Black")]
    [InlineData("https://naadam.co/products/womens-snoopy-hug-cashmere-sweater", "Womens Snoopy Hug Cashmere Sweater")]
    [InlineData("https://www.mejuri.com/shop/products/bold-hoops", "Bold Hoops")]
    [InlineData("https://www.ssense.com/en-us/women/product/the-row/black-leather-bag/1234567", "Black Leather Bag")]
    [InlineData("https://example.com/p/brass_reading_lamp", "Brass Reading Lamp")]
    public void ReadsTheProductNameOutOfTheLink(string url, string expected) =>
        Assert.Equal(expected, UrlSlugParser.TitleFrom(new Uri(url)));

    [Theory]
    [InlineData("https://www2.hm.com/en_us/productpage.1227154001.html")]
    [InlineData("https://example.com/12345678")]
    [InlineData("https://example.com/p/SKU-99321")]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/eg/en")]
    public void DeclinesWhenTheLinkNamesNothing(string url) =>
        Assert.Null(UrlSlugParser.TitleFrom(new Uri(url)));

    [Theory]
    [InlineData("https://shopdoen.com/products/x-y", "Doen")]
    [InlineData("https://www.zara.com/us/en/a-b", "Zara")]
    [InlineData("https://naadam.co/products/a-b", "Naadam")]
    public void GuessesTheBrandFromTheDomain(string url, string expected) =>
        Assert.Equal(expected, UrlSlugParser.BrandFrom(new Uri(url)));

    [Fact]
    public void IsNeverTrustedInFullInk() =>
        Assert.False(new UrlSlugParser().StrongSource);

    [Fact]
    public async Task NeedsNoPageAtAll()
    {
        using var http = new HttpClient();

        var info = await new UrlSlugParser().TryParseAsync(
            new ScrapeContext(new Uri("https://corvid.example/products/brass-reading-lamp"), null, http),
            default);

        Assert.NotNull(info);
        Assert.Equal("Brass Reading Lamp", info.Title);

        Assert.Null(info.Price);
        Assert.Null(info.Currency);
        Assert.Null(info.ImageUrl);
    }
}

public class PageRecoveryTests
{
    [Fact]
    public void TogglesWwwOnAnApexHost() =>
        Assert.Contains(
            PageRecovery.Variants(new Uri("https://example.com/p/thing")),
            variant => variant.Host == "www.example.com");

    [Fact]
    public void StripsWwwFromAPrefixedHost() =>
        Assert.Contains(
            PageRecovery.Variants(new Uri("https://www.example.com/p/thing")),
            variant => variant.Host == "example.com");

    [Fact]
    public void LeavesARealSubdomainAlone()
    {
        var hosts = PageRecovery.Variants(new Uri("https://www2.hm.com/en_us/productpage.1.html"))
            .Select(variant => variant.Host)
            .ToList();

        Assert.DoesNotContain("www.www2.hm.com", hosts);
    }

    [Fact]
    public void TriesTheLinkWithoutItsQuery() =>
        Assert.Contains(
            PageRecovery.Variants(new Uri("https://example.com/p/thing?cS=300&pelement=748863385")),
            variant => variant.Query.Length == 0);
}
