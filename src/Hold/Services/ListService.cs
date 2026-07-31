using System.Security.Cryptography;
using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

/// <summary>
/// A list as someone without an account sees it. Deliberately not <see cref="ListDetail"/>:
/// that record carries the budget, and a shared link must not. Leaving the fields out of the
/// type means a future edit to the shared page cannot accidentally render them.
/// </summary>
public sealed record SharedList(string Name, IReadOnlyList<Item> Items);

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

public sealed class ListService(
    IDbContextFactory<HoldDbContext> factory,
    TimeProvider time,
    CurrentUser user)
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
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // No OrderBy here. Postgres could do it — it orders timestamptz natively, unlike SQLite,
        // which refused to translate DateTimeOffset at all — but the rows are fetched whole
        // anyway to total them, so sorting below costs nothing and keeps one ordering rule in
        // one place.
        var rows = await db.WishLists
            .AsNoTracking()
            .Where(list => list.OwnerId == owner)
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
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // Only what the header needs. The items themselves come from ItemService, which
        // owns their ordering.
        var row = await db.WishLists
            .AsNoTracking()
            .Where(list => list.Id == id && list.OwnerId == owner)
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
            OwnerId = await user.RequireIdAsync(),
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

        var owner = await user.RequireIdAsync();

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == owner,
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

        var owner = await user.RequireIdAsync();

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == owner,
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

    /// <summary>The current share token, or null when the list has never been shared.</summary>
    public async Task<string?> GetShareTokenAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        return await db.WishLists
            .AsNoTracking()
            .Where(list => list.Id == id && list.OwnerId == owner)
            .Select(list => list.ShareToken)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the existing token rather than minting a new one, so opening the share control
    /// twice does not quietly invalidate a link already sent to somebody.
    /// </summary>
    public async Task<string?> ShareAsync(int id, CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == owner,
            cancellationToken);

        if (list is null)
        {
            return null;
        }

        if (list.ShareToken is null)
        {
            list.ShareToken = NewShareToken();
            await db.SaveChangesAsync(cancellationToken);
        }

        return list.ShareToken;
    }

    /// <summary>
    /// Clears the token. Every link handed out so far stops working, which is the whole of
    /// revocation — there is no per-recipient access to withdraw.
    /// </summary>
    public async Task<bool> UnshareAsync(int id, CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == id && row.OwnerId == owner,
            cancellationToken);

        if (list is null)
        {
            return false;
        }

        list.ShareToken = null;
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary>
    /// The one query in this file with no owner scoping, because the caller has no account.
    /// Holding the token is the authorisation. Returns the items in the same order the owner
    /// sees, and nothing else about the list or the person who made it.
    /// </summary>
    public async Task<SharedList?> GetSharedAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var list = await db.WishLists
            .AsNoTracking()
            .Include(row => row.Items)
            .SingleOrDefaultAsync(row => row.ShareToken == token, cancellationToken);

        return list is null
            ? null
            : new SharedList(list.Name, ItemService.Sort(list.Items, time.GetUtcNow()));
    }

    /// <summary>
    /// 128 bits from the cryptographic generator, base64url so it survives a URL untouched.
    /// Not Guid and not Random: this value is the only thing standing between a link and the
    /// contents of somebody's list, so it has to be unguessable rather than merely unique.
    /// </summary>
    private static string NewShareToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

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
