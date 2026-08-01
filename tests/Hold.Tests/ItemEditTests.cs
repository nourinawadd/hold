using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class RetiredRecheckTests
{
    private const string Link = "https://shop.doen.com/products/sylvie-coat";

    private static Item Saved() => new()
    {
        Id = 1,
        Url = Link,
        Title = "Sylvie coat",
        Price = 320m,
        Currency = "USD",
        Category = Category.Outerwear,
        WaitDays = 30,
        SavedAt = DateTimeOffset.Parse("2026-06-01T09:00:00Z"),
        LatestPrice = 280m,
        LatestPriceAt = DateTimeOffset.Parse("2026-07-01T09:00:00Z"),
    };

    private static ItemDraft Draft(
        decimal? price = 320m,
        string currency = "USD",
        string? url = Link) =>
        new(url, "Sylvie coat", null, null, price, currency, Category.Outerwear, 30, null);

    [Fact]
    public void EditingSomethingElseKeepsTheRecheck() =>
        Assert.False(ItemService.RetiresLatestPrice(Saved(), Draft()));

    [Fact]
    public void ANewPriceRetiresTheRecheck() =>
        Assert.True(ItemService.RetiresLatestPrice(Saved(), Draft(price: 295m)));

    [Fact]
    public void ClearingThePriceRetiresTheRecheck() =>
        Assert.True(ItemService.RetiresLatestPrice(Saved(), Draft(price: null)));

    [Fact]
    public void ANewCurrencyRetiresTheRecheck() =>
        Assert.True(ItemService.RetiresLatestPrice(Saved(), Draft(currency: "EUR")));

    [Fact]
    public void TheSameCurrencyInLowercaseDoesNot() =>
        Assert.False(ItemService.RetiresLatestPrice(Saved(), Draft(currency: "usd")));

    [Fact]
    public void SurroundingSpaceOnACurrencyDoesNot() =>
        Assert.False(ItemService.RetiresLatestPrice(Saved(), Draft(currency: " USD ")));

    [Fact]
    public void ANewLinkRetiresTheRecheck() =>
        Assert.True(ItemService.RetiresLatestPrice(
            Saved(),
            Draft(url: "https://margauxny.com/products/the-classic-ballet-flat")));

    [Fact]
    public void RemovingTheLinkRetiresTheRecheck() =>
        Assert.True(ItemService.RetiresLatestPrice(Saved(), Draft(url: null)));

    [Fact]
    public void SurroundingSpaceOnALinkDoesNot() =>
        Assert.False(ItemService.RetiresLatestPrice(Saved(), Draft(url: $"  {Link}  ")));
}

public class ItemEditValidationTests
{
    private static ItemDraft Draft(string title = "Sylvie coat", int waitDays = 30) =>
        new(null, title, null, null, 320m, "USD", Category.Accessories, waitDays, null);

    [Fact]
    public void AnEditIsHeldToTheSameRulesAsAnAdd() =>
        Assert.Equal(
            ItemService.DescribeProblem(Draft(title: "  ")),
            ItemService.DescribeProblem(new ItemDraft(
                null, "  ", null, null, 320m, "USD", Category.Accessories, 30, null)));

    [Fact]
    public void AWaitCanBeChangedToAnyAllowedLength()
    {
        Assert.Null(ItemService.DescribeProblem(Draft(waitDays: ItemService.MinWaitDays)));
        Assert.Null(ItemService.DescribeProblem(Draft(waitDays: ItemService.MaxWaitDays)));
    }

    [Fact]
    public void AWaitOutsideThatRangeIsStillRefused()
    {
        Assert.NotNull(ItemService.DescribeProblem(Draft(waitDays: 0)));
        Assert.NotNull(ItemService.DescribeProblem(Draft(waitDays: ItemService.MaxWaitDays + 1)));
    }
}
