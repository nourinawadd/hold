using Hold.Services;

namespace Hold.Tests;

public class PriceCheckTests
{
    [Fact]
    public void ADropIsReportedAsADrop()
    {
        var check = new PriceCheck(89.00m, 62.00m, "USD");

        Assert.True(check.Dropped);
        Assert.False(check.Rose);
        Assert.Equal(30, check.PercentChange);
        Assert.Equal("Down 30% since you saved it.", check.Describe());
    }

    [Fact]
    public void ARiseIsReportedAsARise()
    {
        var check = new PriceCheck(100.00m, 125.00m, "USD");

        Assert.True(check.Rose);
        Assert.Equal(25, check.PercentChange);
        Assert.Equal("Up 25% since you saved it.", check.Describe());
    }

    [Fact]
    public void NoMovementSaysSo()
    {
        var check = new PriceCheck(89.00m, 89.00m, "USD");

        Assert.False(check.Dropped);
        Assert.False(check.Rose);
        Assert.Equal("The same as when you saved it.", check.Describe());
    }

    [Fact]
    public void AFreeItemDoesNotDivideByZero() =>
        Assert.Equal(0, new PriceCheck(0m, 10m, "USD").PercentChange);

    [Fact]
    public void ThePercentageIsAlwaysPositiveAndTheDirectionCarriesTheSign()
    {
        Assert.Equal(30, new PriceCheck(89.00m, 62.00m, "USD").PercentChange);
        Assert.Equal(30, new PriceCheck(62.00m, 80.60m, "USD").PercentChange);
    }

    [Fact]
    public void HalfPercentagesRoundAwayFromZero() =>
        Assert.Equal(4, new PriceCheck(200m, 193m, "USD").PercentChange);
}
