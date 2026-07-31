using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class EstimatedPriceTests
{
    [Theory]
    [InlineData("https://www.pinterest.com/pin/1234567890/")]
    [InlineData("https://pinterest.com/pin/1234567890/")]
    [InlineData("https://uk.pinterest.com/pin/1234567890/")]
    [InlineData("https://www.pinterest.co.uk/pin/1234567890/")]
    [InlineData("https://pin.it/aBcDeF")]
    public void PinterestLinksAreEstimates(string url) =>
        Assert.True(EstimatedPrice.LikelyEstimate(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoLinkIsAnEstimate(string? url) =>
        Assert.True(EstimatedPrice.LikelyEstimate(url));

    [Theory]
    [InlineData("https://shop.doen.com/products/sylvie-coat")]
    [InlineData("https://margauxny.com/products/the-classic-ballet-flat")]
    [InlineData("https://mejuri.com/products/bold-hoops")]
    public void AShopLinkIsNotAnEstimate(string url) =>
        Assert.False(EstimatedPrice.LikelyEstimate(url));

    [Theory]
    [InlineData("https://pinterest-clone.com/pin/1")]
    [InlineData("https://notpinterest.com/pin/1")]
    [InlineData("https://mypinterest.example/pin/1")]
    public void ALookalikeHostIsNotPinterest(string url) =>
        Assert.False(EstimatedPrice.LikelyEstimate(url));

    [Fact]
    public void SomethingUnparseableIsTreatedAsAnEstimate() =>
        Assert.True(EstimatedPrice.LikelyEstimate("not a link at all"));
}

public class LinklessItemTests
{
    private static ItemDraft Draft(string? url) =>
        new(url, "Kitchen shelf", null, null, 45m, "USD", Category.Projects, 30, null);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnItemNoLongerNeedsALink(string? url) =>
        Assert.Null(ItemService.DescribeProblem(Draft(url)));

    [Fact]
    public void ALinkThatIsSuppliedMustStillBeAWebAddress() =>
        Assert.NotNull(ItemService.DescribeProblem(Draft("shop.doen.com/products/sylvie-coat")));

    [Fact]
    public void ALinkThatIsSuppliedMustStillFitTheColumn() =>
        Assert.NotNull(ItemService.DescribeProblem(
            Draft("https://shop.example/" + new string('x', ItemService.UrlMaxLength))));

    [Fact]
    public void AnItemStillNeedsAName() =>
        Assert.NotNull(ItemService.DescribeProblem(
            new ItemDraft(null, "  ", null, null, null, "USD", Category.Projects, 30, null)));

    [Fact]
    public void AValidShopLinkIsStillAccepted() =>
        Assert.Null(ItemService.DescribeProblem(Draft("https://shop.doen.com/products/sylvie-coat")));

    [Fact]
    public void ADraftIsAQuoteUnlessSaidOtherwise() =>
        Assert.False(Draft("https://shop.doen.com/products/sylvie-coat").PriceIsEstimate);
}

public class EstimatedTotalTests
{
    [Fact]
    public void ATotalWithNoEstimatesReadsPlainly() =>
        Assert.Equal("1,240.00 USD", new CurrencyTotal("USD", 1240m).Display());

    [Fact]
    public void ATotalCarryingAnEstimateIsMarked() =>
        Assert.Equal("≈ 1,240.00 USD", new CurrencyTotal("USD", 1240m, 3).Display());

    [Fact]
    public void EstimatesAreCountedNotExcluded()
    {
        var total = new CurrencyTotal("USD", 1240m, 3);

        Assert.Equal(1240m, total.Amount);
        Assert.True(total.HasEstimates);
    }
}
