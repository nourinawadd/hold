using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public sealed record Holder(string Name, string Initial, string? Email, DateTimeOffset? Since);

public sealed record Snapshot(
    Holder Holder,
    int ItemsHeld,
    int Waiting,
    int Ready,
    int Bought,
    int LetGo,
    int Lists,
    IReadOnlyList<CurrencyTotal> NotSpent,
    IReadOnlyList<CurrencyTotal> Committed,
    int? MedianDaysBeforeLettingGo,
    Item? LongestHold)
{
    public bool Empty => ItemsHeld == 0;
}

public sealed class StatsService(
    IDbContextFactory<HoldDbContext> factory,
    TimeProvider time,
    CurrentUser user)
{
    public async Task<Snapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var account = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == owner, cancellationToken);

        var items = await db.Items
            .AsNoTracking()
            .Include(item => item.WishList)
            .Where(item => item.WishList.OwnerId == owner)
            .ToListAsync(cancellationToken);

        var lists = await db.WishLists
            .AsNoTracking()
            .CountAsync(list => list.OwnerId == owner, cancellationToken);

        var now = time.GetUtcNow();

        var letGo = items.Where(item => item.Status == ItemStatus.LetGo).ToList();
        var bought = items.Where(item => item.Status == ItemStatus.Bought).ToList();
        var waiting = items.Where(item => item.Status == ItemStatus.Waiting).ToList();

        return new Snapshot(
            Describe(account),
            items.Count,
            waiting.Count,
            waiting.Count(item => item.IsReady(now)),
            bought.Count,
            letGo.Count,
            lists,
            Totals(letGo),
            Totals(waiting),
            MedianDays(letGo),
            waiting.OrderBy(item => item.SavedAt).FirstOrDefault());
    }

    public static Holder Describe(User? account)
    {
        if (account is null)
        {
            return new Holder("You", "?", null, null);
        }

        var name = string.IsNullOrWhiteSpace(account.DisplayName)
            ? LocalPart(account.Email)
            : account.DisplayName.Trim();

        return new Holder(name, Initial(name), account.Email, account.CreatedAt);
    }

    public static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;

        return string.IsNullOrWhiteSpace(local) ? "You" : local;
    }

    public static string Initial(string name)
    {
        var letter = name.FirstOrDefault(char.IsLetter);

        return letter == default ? "?" : char.ToUpperInvariant(letter).ToString();
    }

    public static int? MedianDays(IReadOnlyList<Item> closed)
    {
        var spans = closed
            .Where(item => item.ClosedAt is not null)
            .Select(item => (int)(item.ClosedAt!.Value - item.SavedAt).TotalDays)
            .OrderBy(days => days)
            .ToList();

        if (spans.Count == 0)
        {
            return null;
        }

        var middle = spans.Count / 2;

        return spans.Count % 2 == 1
            ? spans[middle]
            : (spans[middle - 1] + spans[middle]) / 2;
    }

    private static List<CurrencyTotal> Totals(IEnumerable<Item> items) =>
        items
            .Where(item => item.Price.HasValue)
            .GroupBy(item => item.Currency)
            .Select(group => new CurrencyTotal(
                group.Key,
                group.Sum(item => item.Price!.Value),
                group.Count(item => item.PriceIsEstimate)))
            .OrderBy(total => total.Currency)
            .ToList();
}
