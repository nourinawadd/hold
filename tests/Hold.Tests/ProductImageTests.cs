namespace Hold.Tests;

public class ProductImageTests
{
    private const string Shopify =
        "https://cdn.shopify.com/s/files/1/0950/9820/products/Slips_046.jpg?v=1685025014";

    private const string PullAndBear =
        "https://static.pullandbear.net/assets/public/5795/07670351700-A6M.jpg?ts=1781779416111&w=972&f=auto";

    [Fact]
    public void AddsAWidthToAShopifyUrl()
    {
        var sized = ProductImage.AtWidth(Shopify, 160);

        Assert.Contains("width=160", sized);

        Assert.Contains("v=1685025014", sized);
    }

    [Fact]
    public void RewritesAWidthThatIsAlreadyThere()
    {
        var sized = ProductImage.AtWidth(PullAndBear, 160)!;

        Assert.Contains("w=160", sized);
        Assert.DoesNotContain("w=972", sized);

        Assert.Single(sized.Split('&'), part => part.StartsWith("w=", StringComparison.Ordinal));

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
        var once = ProductImage.AtWidth(Shopify, 160);
        var twice = ProductImage.AtWidth(once, 160);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void AsksForMoreThanItDraws() =>
        Assert.True(ProductImage.ThumbnailWidth >= 128 && ProductImage.CardWidth > ProductImage.ThumbnailWidth);
}
