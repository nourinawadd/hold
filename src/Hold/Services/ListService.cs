using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

/// <summary>A total in one currency. Amounts are never converted between currencies.</summary>
public sealed record CurrencyTotal(string Currency, decimal Amount);

public sealed record ListSummary(
    int Id,
    string Name,
    int ItemCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CurrencyTotal> Totals,
    /// <summary>Image URLs for the card's thumbnail strip, already filtered and capped.</summary>
    IReadOnlyList<string> Images);

public sealed record ListDetail(
    int Id,
    string Name,
    string? Description,
    decimal? BudgetAmount,
    string? BudgetCurrency,
    int ItemCount,
    IReadOnlyList<CurrencyTotal> Totals);

public sealed class ListService(IDbContextFactory<HoldDbContext> factory, TimeProvider time)
{
    // Mirrors the column limits in HoldDbContext.OnModelCreating, so an over-long value is
    // refused with a sentence rather than truncated by SQLite.
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    /// <summary>Slots in a list card's thumbnail strip.</summary>
    public const int ThumbnailSlots = 5;

    /// <summary>
    /// The one place the naming rules live. The form calls this to show a message; the
    /// mutations below call it again so a bad value cannot reach the database by another
    /// route.
    /// </summary>
    public static string? DescribeProblem(string? name, string? description)
    {
        var trimmedName = name?.Trim();

        if (string.IsNullOrEmpty(trimmedName))
        {
            return "A list needs a name.";
        }

        if (trimmedName.Length > NameMaxLength)
        {
            return $"That name is {trimmedName.Length} characters. Keep it under {NameMaxLength}.";
        }

        var trimmedDescription = description?.Trim();

        if (trimmedDescription?.Length > DescriptionMaxLength)
        {
            return $"That description is {trimmedDescription.Length} characters. Keep it under {DescriptionMaxLength}.";
        }

        return null;
    }

    public async Task<IReadOnlyList<ListSummary>> GetSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // No OrderBy here: the SQLite provider refuses to translate DateTimeOffset in an
        // ORDER BY clause at all, so date ordering happens in memory below — same rule as
        // money, for a different reason.
        var rows = await db.WishLists
            .AsNoTracking()
            .Where(list => list.OwnerId == WishList.DefaultOwnerId)
            .Select(list => new
            {
                list.Id,
                list.Name,
                list.UpdatedAt,
                Items = list.Items
                    .Select(item => new { item.Price, item.Currency, item.ImageUrl })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        // Money is TEXT as well, where "9.00" sorts after "100.00" and SUM() coerces to a
        // float. The query above materialises first so all of the arithmetic below runs in
        // memory, on decimal.
        return rows
            .OrderByDescending(row => row.UpdatedAt)
            .Select(row => new ListSummary(
                row.Id,
                row.Name,
                row.Items.Count,
                row.UpdatedAt,
                Totals(row.Items.Select(entry => (entry.Price, entry.Currency))),
                // Only the items that actually have a picture, so the strip fills from the
                // left whether those items were saved first or last.
                row.Items
                    .Select(entry => entry.ImageUrl)
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Take(ThumbnailSlots)
                    .ToList()!))
            .ToList();
    }

    public async Task<ListDetail?> GetDetailAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // Only what the header needs. The items themselves come from ItemService, which
        // owns their ordering.
        var row = await db.WishLists
            .AsNoTracking()
            .Where(list => list.Id == id && list.OwnerId == WishList.DefaultOwnerId)
            .Select(list => new
            {
                list.Id,
                list.Name,
                list.Description,
                list.BudgetAmount,
                list.BudgetCurrency,
                Items = list.Items
                    .Select(item => new { item.Price, item.Currency })
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new ListDetail(
            row.Id,
            row.Name,
            row.Description,
            row.BudgetAmount,
            row.BudgetCurrency,
            row.Items.Count,
            Totals(row.Items.Select(item => (item.Price, item.Currency))));
    }

    public async Task<int> CreateAsync(
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        Guard(name, description);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var now = time.GetUtcNow();

        var list = new WishList
        {
            Name = name.Trim(),
            Description = Clean(description),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.WishLists.Add(list);
        await db.SaveChangesAsync(cancellationToken);

        return list.Id;
    }

    /// <summary>Returns false when the list no longer exists — a stale card, not an error.</summary>
    public async Task<bool> RenameAsync(
        int id,
        string name,
        string? description,
        CancellationToken cancellationToken = default)
    {
        Guard(name, description);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == WishList.DefaultOwnerId,
            cancellationToken);

        if (list is null)
        {
            return false;
        }

        list.Name = name.Trim();
        list.Description = Clean(description);

        // A rename counts as activity, so the card carries a fresh time and rises to the
        // top of the grid.
        list.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == WishList.DefaultOwnerId,
            cancellationToken);

        if (list is null)
        {
            return false;
        }

        // The items go with it, by the cascade configured in HoldDbContext.
        db.WishLists.Remove(list);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static void Guard(string? name, string? description)
    {
        var problem = DescribeProblem(name, description);

        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(name));
        }
    }

    private static string? Clean(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Grouped in memory on purpose. Callers must have materialised their rows first —
    /// decimal is TEXT in SQLite, so a SQL SUM would coerce money to a float.
    /// </summary>
    private static List<CurrencyTotal> Totals(IEnumerable<(decimal? Price, string Currency)> items) =>
        items
            .Where(item => item.Price.HasValue)
            .GroupBy(item => item.Currency)
            .Select(group => new CurrencyTotal(group.Key, group.Sum(item => item.Price!.Value)))
            .OrderBy(total => total.Currency)
            .ToList();
}
