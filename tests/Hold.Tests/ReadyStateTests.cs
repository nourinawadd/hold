using Hold.Data;
using Hold.Services;

namespace Hold.Tests;

public class ReadyStateTests
{
    private static readonly DateTimeOffset Saved = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Item Waiting(int waitDays = 30, DateTimeOffset? savedAt = null) => new()
    {
        Url = "https://shop.example/thing",
        Title = "Thing",
        Currency = "USD",
        Category = Category.Other,
        WaitDays = waitDays,
        SavedAt = savedAt ?? Saved,
        Status = ItemStatus.Waiting,
    };

    [Fact]
    public void NotReadyOnDay29() =>
        Assert.False(Waiting().IsReady(Saved.AddDays(29)));

    [Fact]
    public void ReadyOnDay30() =>
        Assert.True(Waiting().IsReady(Saved.AddDays(30)));

    [Fact]
    public void ReadyOnDay31() =>
        Assert.True(Waiting().IsReady(Saved.AddDays(31)));

    [Fact]
    public void NotReadyOneTickBeforeDay30() =>
        Assert.False(Waiting().IsReady(Saved.AddDays(30).AddTicks(-1)));

    [Fact]
    public void ReadyExactlyOnTheTick() =>
        Assert.True(Waiting().IsReady(Saved.AddDays(30)));

    [Fact]
    public void ReadyAtIsSavedPlusWait() =>
        Assert.Equal(Saved.AddDays(30), Waiting().ReadyAt);

    [Fact]
    public void ADayWaitIsReadyTheNextDay()
    {
        var item = Waiting(waitDays: 1);

        Assert.False(item.IsReady(Saved.AddHours(23)));
        Assert.True(item.IsReady(Saved.AddDays(1)));
    }

    [Theory]
    [InlineData(ItemStatus.Bought)]
    [InlineData(ItemStatus.LetGo)]
    public void AClosedItemIsNeverReady(ItemStatus status)
    {
        var item = Waiting();
        item.Status = status;
        item.ClosedAt = Saved.AddDays(5);

        Assert.False(item.IsReady(Saved.AddDays(999)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(30)]
    [InlineData(365)]
    public void DaysWaitedCountsWholeDays(int days) =>
        Assert.Equal(days, Waiting().DaysWaited(Saved.AddDays(days)));

    [Fact]
    public void DaysWaitedTruncatesAPartDay() =>
        Assert.Equal(0, Waiting().DaysWaited(Saved.AddHours(23)));

    [Fact]
    public void AClosedItemFreezesAtItsDecision()
    {
        var item = Waiting();
        item.Status = ItemStatus.Bought;
        item.ClosedAt = Saved.AddDays(23);

        Assert.Equal(23, item.DaysWaited(item.ClosedAt ?? Saved.AddDays(400)));
    }

    [Fact]
    public void ReadyItemsComeFirstEvenWhenSavedLater()
    {
        var now = Saved.AddDays(40);

        var readyButNew = Waiting(waitDays: 1, savedAt: Saved.AddDays(38));
        readyButNew.Title = "ready";

        var waitingButOld = Waiting(waitDays: 200, savedAt: Saved);
        waitingButOld.Title = "old";

        var waitingMiddle = Waiting(waitDays: 200, savedAt: Saved.AddDays(10));
        waitingMiddle.Title = "middle";

        var sorted = ItemService.Sort([waitingButOld, waitingMiddle, readyButNew], now);

        Assert.Equal(["ready", "old", "middle"], sorted.Select(item => item.Title));
    }

    [Fact]
    public void LongestHeldComesFirstAmongItemsAtTheSameStage()
    {
        var now = Saved.AddDays(10);

        var newer = Waiting(waitDays: 90, savedAt: Saved.AddDays(5));
        newer.Title = "newer";

        var older = Waiting(waitDays: 90, savedAt: Saved);
        older.Title = "older";

        var sorted = ItemService.Sort([newer, older], now);

        Assert.Equal(["older", "newer"], sorted.Select(item => item.Title));
    }

    [Fact]
    public void ClosedItemsSinkBelowEverything()
    {
        var now = Saved.AddDays(40);

        var bought = Waiting(waitDays: 1, savedAt: Saved);
        bought.Title = "bought";
        bought.Status = ItemStatus.Bought;
        bought.ClosedAt = Saved.AddDays(2);

        var stillWaiting = Waiting(waitDays: 200, savedAt: Saved.AddDays(30));
        stillWaiting.Title = "waiting";

        var sorted = ItemService.Sort([bought, stillWaiting], now);

        Assert.Equal(["waiting", "bought"], sorted.Select(item => item.Title));
    }

    [Fact]
    public void SortIsPureAndLeavesTheInputAlone()
    {
        var items = new[] { Waiting(savedAt: Saved.AddDays(5)), Waiting(savedAt: Saved) };
        var before = items.ToArray();

        ItemService.Sort(items, Saved.AddDays(10));

        Assert.Equal(before, items);
    }
}
