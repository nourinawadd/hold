using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class HolderTests
{
    private static User Account(string email, string? displayName = null) => new()
    {
        GoogleSubject = "sub",
        Email = email,
        DisplayName = displayName,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void ADisplayNameIsPreferred() =>
        Assert.Equal("Nourin", StatsService.Describe(Account("someone@example.com", "Nourin")).Name);

    [Fact]
    public void AMissingDisplayNameFallsBackToTheEmailLocalPart() =>
        Assert.Equal("nourin", StatsService.Describe(Account("nourin@example.com")).Name);

    [Fact]
    public void ABlankDisplayNameFallsBackToo() =>
        Assert.Equal("nourin", StatsService.Describe(Account("nourin@example.com", "   ")).Name);

    [Fact]
    public void TheInitialIsTheFirstLetter() =>
        Assert.Equal("N", StatsService.Describe(Account("nourin@example.com")).Initial);

    [Fact]
    public void ANameStartingWithASymbolStillYieldsALetter() =>
        Assert.Equal("A", StatsService.Initial("_alice"));

    [Fact]
    public void ANameWithNoLettersFallsBackToAQuestionMark() =>
        Assert.Equal("?", StatsService.Initial("123"));

    [Fact]
    public void AnEmailWithNoAtSignIsUsedWhole() =>
        Assert.Equal("nourin", StatsService.LocalPart("nourin"));

    [Fact]
    public void NoAccountStillDescribesSomebody()
    {
        var holder = StatsService.Describe(null);

        Assert.Equal("You", holder.Name);
        Assert.Null(holder.Email);
        Assert.Null(holder.Since);
    }
}

public class MedianWaitTests
{
    private static Item Closed(int daysHeld) => new()
    {
        Title = "Thing",
        Currency = "USD",
        Category = Category.Other,
        WaitDays = 30,
        SavedAt = DateTimeOffset.UnixEpoch,
        ClosedAt = DateTimeOffset.UnixEpoch.AddDays(daysHeld),
        Status = ItemStatus.LetGo,
    };

    [Fact]
    public void NothingClosedHasNoMedian() =>
        Assert.Null(StatsService.MedianDays([]));

    [Fact]
    public void AnOddCountTakesTheMiddle() =>
        Assert.Equal(30, StatsService.MedianDays([Closed(10), Closed(30), Closed(90)]));

    [Fact]
    public void AnEvenCountAveragesTheTwoMiddle() =>
        Assert.Equal(20, StatsService.MedianDays([Closed(10), Closed(30)]));

    [Fact]
    public void OrderOfInputDoesNotMatter() =>
        Assert.Equal(30, StatsService.MedianDays([Closed(90), Closed(10), Closed(30)]));

    [Fact]
    public void AnItemWithNoClosingDateIsIgnored()
    {
        var open = Closed(10);
        open.ClosedAt = null;

        Assert.Equal(30, StatsService.MedianDays([open, Closed(30)]));
    }
}
