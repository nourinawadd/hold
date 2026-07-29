namespace Hold.Tests;

public class ProductImageTests
{
    private const string Shopify =
        "https://cdn.shopify.com/s/files/1/0950/9820/products/Slips_046.jpg?v=1685025014";

    // Inditex already sends a width, so rewriting it needs no per-shop knowledge.
    private const string PullAndBear =
        "https://static.pullandbear.net/assets/public/5795/07670351700-A6M.jpg?ts=1781779416111&w=972&f=auto";

    [Fact]
    public void AddsAWidthToAShopifyUrl()
    {
        var sized = ProductImage.AtWidth(Shopify, 160);

        Assert.Contains("width=160", sized);

        // The cache key must survive, or every request is a fresh download.
        Assert.Contains("v=1685025014", sized);
    }

    [Fact]
    public void RewritesAWidthThatIsAlreadyThere()
    {
        var sized = ProductImage.AtWidth(PullAndBear, 160)!;

        Assert.Contains("w=160", sized);
        Assert.DoesNotContain("w=972", sized);

        // Exactly one width parameter — a duplicate is ambiguous to the CDN.
        Assert.Single(sized.Split('&'), part => part.StartsWith("w=", StringComparison.Ordinal));

        // Other parameters are left alone.
        Assert.Contains("f=auto", sized);
        Assert.Contains("ts=1781779416111", sized);
    }

    [Fact]
    public void HandlesTheShopifyCdnShopPathShape() =>
        Assert.Contains(
            "width=480",
            ProductImage.AtWidth("https://shopdoen.com/cdn/shop/products/a.jpg?v=1", 480)!);

    [Fact]
    public void LeavesAnUnknownCdnExactlyAsGiven()
    {
        // An unrecognised host might read an unexpected parameter as a signature mismatch
        // or a cache-buster, so nothing is added speculatively.
        const string url = "https://images.example.com/product/a.jpg";

        Assert.Equal(url, ProductImage.AtWidth(url, 160));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path.jpg")]
    public void PassesThroughAnythingItCannotParse(string? url) =>
        Assert.Equal(url, ProductImage.AtWidth(url, 160));

    [Fact]
    public void IsIdempotent()
    {
        // Re-rendering the same card must not stack parameters.
        var once = ProductImage.AtWidth(Shopify, 160);
        var twice = ProductImage.AtWidth(once, 160);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AsksForMoreThanItDraws() =>
        // Roughly 2x the drawn size, so the picture stays sharp on a dense screen.
        Assert.True(ProductImage.ThumbnailWidth >= 128 && ProductImage.CardWidth > ProductImage.ThumbnailWidth);
}
