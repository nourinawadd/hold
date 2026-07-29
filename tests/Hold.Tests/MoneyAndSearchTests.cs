using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class BudgetValidationTests
{
    private static ListService.ListDraft Draft(decimal? amount = null, string? currency = null) =>
        new("Wishlist", null, amount, currency);

    [Fact]
    public void ABudgetIsOptional() =>
        Assert.Null(ListService.DescribeProblem(Draft()));

    [Fact]
    public void ANegativeBudgetIsRefused() =>
        // A negative budget is not a constraint.
        Assert.NotNull(ListService.DescribeProblem(Draft(-1m, "USD")));

    [Fact]
    public void ZeroIsAllowed() =>
        // "Spend nothing" is a real intention, and the bar shows it as immediately over.
        Assert.Null(ListService.DescribeProblem(Draft(0m, "USD")));

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U5D")]
    public void CurrencyMustBeThreeLetters(string currency) =>
        Assert.NotNull(ListService.DescribeProblem(Draft(100m, currency)));

    [Fact]
    public void AnAmountWithNoCurrencyTakesThePreferredOne()
    {
        var (amount, currency) = ListService.SettleBudget(1500m, null, "EGP");

        Assert.Equal(1500m, amount);
        Assert.Equal("EGP", currency);
    }

    [Fact]
    public void ACurrencyWithNoAmountIsCleared()
    {
        // A currency governs nothing on its own.
        var (amount, currency) = ListService.SettleBudget(null, "USD", "USD");

        Assert.Null(amount);
        Assert.Null(currency);
    }

    [Fact]
    public void CurrencyIsUpperCased()
    {
        var (_, currency) = ListService.SettleBudget(100m, "usd", "EGP");

        Assert.Equal("USD", currency);
    }
}

public class ItemSearchTests
{
    private static Item Item(string title, string? brand = null, string? listName = null) => new()
    {
        Url = "https://shop.example/x",
        Title = title,
        Brand = brand,
        Currency = "USD",
        Category = Category.Other,
        WaitDays = 30,
        SavedAt = DateTimeOffset.UnixEpoch,
        Status = ItemStatus.Waiting,
        WishList = listName is null ? null! : new WishList { Name = listName },
    };

    [Fact]
    public void MatchesOnTitle() =>
        Assert.True(ItemService.Matches(Item("Sylvie Coat"), "sylvie"));

    [Fact]
    public void MatchesOnBrand() =>
        Assert.True(ItemService.Matches(Item("Coat", brand: "Margaux"), "margaux"));

    [Fact]
    public void MatchesOnTheListName() =>
        // The third field CLAUDE.md:202 names.
        Assert.True(ItemService.Matches(Item("Scarf", listName: "Gifts"), "gifts"));

    [Fact]
    public void IgnoresAccents()
    {
        // Half the seeded brands are accented. An ordinal comparison would miss this, and
        // nobody types the circumflex.
        Assert.True(ItemService.Matches(Item("Coat", brand: "Dôen"), "doen"));
        Assert.True(ItemService.Matches(Item("Coat", brand: "Doen"), "dôen"));
    }

    [Fact]
    public void IgnoresCase() =>
        Assert.True(ItemService.Matches(Item("SYLVIE COAT"), "sylvie"));

    [Fact]
    public void IgnoresSurroundingSpace() =>
        Assert.True(ItemService.Matches(Item("Sylvie Coat"), "  sylvie  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptySearchMatchesEverything(string? search) =>
        Assert.True(ItemService.Matches(Item("Anything"), search));

    [Fact]
    public void DoesNotMatchWhatIsNotThere() =>
        Assert.False(ItemService.Matches(Item("Sylvie Coat", brand: "Dôen", listName: "Wishlist"), "trousers"));

    [Fact]
    public void SurvivesAnItemWithNoBrandOrList() =>
        // Brand is nullable and WishList is not loaded on every path.
        Assert.False(ItemService.Matches(Item("Coat"), "margaux"));
}

/// <summary>
/// The bar's arithmetic, which is the part that must never be wrong: it decides whether the
/// user believes they have room to spend.
/// </summary>
public class BudgetBarMathTests
{
    private static decimal Spent(string currency, params CurrencyTotal[] totals) =>
        totals.Where(total => total.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            .Sum(total => total.Amount);

    [Fact]
    public void CountsOnlyTheBudgetsOwnCurrency()
    {
        // Wishlist really does hold both. Folding the EGP in would require a rate the app
        // deliberately does not have.
        var totals = new[] { new CurrencyTotal("USD", 1272m), new CurrencyTotal("EGP", 2590m) };

        Assert.Equal(1272m, Spent("USD", totals));
    }

    [Fact]
    public void ACurrencyWithNoItemsSpendsNothing() =>
        Assert.Equal(0m, Spent("GBP", new CurrencyTotal("USD", 100m)));

    [Fact]
    public void ExactlyAtBudgetIsNotOver()
    {
        const decimal budget = 1500m;
        var spent = Spent("USD", new CurrencyTotal("USD", 1500m));

        // The boundary: spending your whole budget is not overspending it.
        Assert.False(spent > budget);
    }

    [Fact]
    public void OneCentOverIsOver()
    {
        const decimal budget = 1500m;
        var spent = Spent("USD", new CurrencyTotal("USD", 1500.01m));

        Assert.True(spent > budget);
    }

    [Fact]
    public void MoneyStaysExact()
    {
        var totals = new[] { new CurrencyTotal("USD", 10.10m), new CurrencyTotal("USD", 20.20m) };

        // decimal, never double — 10.10 + 20.20 is 30.30 and not 30.299999999999997.
        Assert.Equal(30.30m, Spent("USD", totals));
    }
}
