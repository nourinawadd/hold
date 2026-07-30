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
    // refused with a sentence the user can act on. Postgres would otherwise reject the insert
    // outright — it does not truncate to fit the way SQLite did — and that surfaces as an
    // exception rather than as validation.
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    /// <summary>Slots in a list card's thumbnail strip.</summary>
    public const int ThumbnailSlots = 5;

    /// <summary>What the create and rename form collects.</summary>
    public sealed record ListDraft(
        string Name,
        string? Description,
        decimal? BudgetAmount,
        string? BudgetCurrency);

    /// <summary>
    /// The one place the naming and budget rules live. The form calls this to show a message;
    /// the mutations below call it again so a bad value cannot reach the database by another
    /// route.
    /// </summary>
    public static string? DescribeProblem(ListDraft draft)
    {
        var trimmedName = draft.Name?.Trim();

        if (string.IsNullOrEmpty(trimmedName))
        {
            return "A list needs a name.";
        }

        if (trimmedName.Length > NameMaxLength)
        {
            return $"That name is {trimmedName.Length} characters. Keep it under {NameMaxLength}.";
        }

        var trimmedDescription = draft.Description?.Trim();

        if (trimmedDescription?.Length > DescriptionMaxLength)
        {
            return $"That description is {trimmedDescription.Length} characters. Keep it under {DescriptionMaxLength}.";
        }

        if (draft.BudgetAmount is < 0)
        {
            return "A budget cannot be negative.";
        }

        var currency = draft.BudgetCurrency?.Trim();

        // Three letters, because the column is char(3) and every currency code is.
        if (!string.IsNullOrEmpty(currency) && (currency.Length != 3 || !currency.All(char.IsLetter)))
        {
            return "Currency should be a three-letter code, like USD.";
        }

        return null;
    }

    /// <summary>
    /// An amount with no currency takes the preferred one — a number alone cannot say what it
    /// counts. A currency with no amount is cleared, because a currency governs nothing on its
    /// own.
    /// </summary>
    public static (decimal? Amount, string? Currency) SettleBudget(
        decimal? amount,
        string? currency,
        string preferredCurrency)
    {
        if (amount is null)
        {
            return (null, null);
        }

        var settled = Clean(currency) ?? preferredCurrency;

        return (amount, settled.Trim().ToUpperInvariant());
    }

    public async Task<IReadOnlyList<ListSummary>> GetSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // No OrderBy here. Postgres could do it — it orders timestamptz natively, unlike SQLite,
        // which refused to translate DateTimeOffset at all — but the rows are fetched whole
        // anyway to total them, so sorting below costs nothing and keeps one ordering rule in
        // one place.
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

        // Totals are computed here rather than by SQL because they are grouped by currency and
        // never converted, which is a per-list shape rather than one aggregate. The query above
        // materialises first so the arithmetic below runs on decimal.
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
        ListDraft draft,
        string preferredCurrency = "USD",
        CancellationToken cancellationToken = default)
    {
        Guard(draft);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var now = time.GetUtcNow();
        var (amount, currency) = SettleBudget(draft.BudgetAmount, draft.BudgetCurrency, preferredCurrency);

        var list = new WishList
        {
            Name = draft.Name.Trim(),
            Description = Clean(draft.Description),
            BudgetAmount = amount,
            BudgetCurrency = currency,
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
        ListDraft draft,
        string preferredCurrency = "USD",
        CancellationToken cancellationToken = default)
    {
        Guard(draft);

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == WishList.DefaultOwnerId,
            cancellationToken);

        if (list is null)
        {
            return false;
        }

        var (amount, currency) = SettleBudget(draft.BudgetAmount, draft.BudgetCurrency, preferredCurrency);

        list.Name = draft.Name.Trim();
        list.Description = Clean(draft.Description);
        list.BudgetAmount = amount;
        list.BudgetCurrency = currency;

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

    private static void Guard(ListDraft draft)
    {
        var problem = DescribeProblem(draft);

        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(draft));
        }
    }

    private static string? Clean(string? description)
    {
        var trimmed = description?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Grouped in memory on purpose: totals are per currency and never converted, so this is a
    /// handful of sums over rows the caller has already fetched rather than one aggregate worth
    /// a round trip. Callers must have materialised their rows first.
    /// </summary>
    private static List<CurrencyTotal> Totals(IEnumerable<(decimal? Price, string Currency)> items) =>
        items
            .Where(item => item.Price.HasValue)
            .GroupBy(item => item.Currency)
            .Select(group => new CurrencyTotal(group.Key, group.Sum(item => item.Price!.Value)))
            .OrderBy(total => total.Currency)
            .ToList();
}
