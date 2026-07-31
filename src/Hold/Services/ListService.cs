using System.Security.Cryptography;
using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public sealed record SharedList(string Name, IReadOnlyList<Item> Items);

public sealed record CurrencyTotal(string Currency, decimal Amount, int EstimateCount = 0)
{
    public bool HasEstimates => EstimateCount > 0;

    public string Display() => HasEstimates ? $"≈ {Amount:N2} {Currency}" : $"{Amount:N2} {Currency}";
}

public sealed record ListSummary(
    int Id,
    string Name,
    int ItemCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CurrencyTotal> Totals,
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
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public const int ThumbnailSlots = 5;

    public sealed record ListDraft(
        string Name,
        string? Description,
        decimal? BudgetAmount,
        string? BudgetCurrency);

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

        if (!string.IsNullOrEmpty(currency) && (currency.Length != 3 || !currency.All(char.IsLetter)))
        {
            return "Currency should be a three-letter code, like USD.";
        }

        return null;
    }

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

        var rows = await db.WishLists
            .AsNoTracking()
            .Where(list => list.OwnerId == owner)
            .Select(list => new
            {
                list.Id,
                list.Name,
                list.UpdatedAt,
                Items = list.Items
                    .Select(item => new { item.Price, item.Currency, item.PriceIsEstimate, item.ImageUrl })
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.UpdatedAt)
            .Select(row => new ListSummary(
                row.Id,
                row.Name,
                row.Items.Count,
                row.UpdatedAt,
                Totals(row.Items.Select(entry => (entry.Price, entry.Currency, entry.PriceIsEstimate))),
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
                    .Select(item => new { item.Price, item.Currency, item.PriceIsEstimate })
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
            Totals(row.Items.Select(item => (item.Price, item.Currency, item.PriceIsEstimate))));
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

        db.WishLists.Remove(list);
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

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

    private static List<CurrencyTotal> Totals(
        IEnumerable<(decimal? Price, string Currency, bool IsEstimate)> items) =>
        items
            .Where(item => item.Price.HasValue)
            .GroupBy(item => item.Currency)
            .Select(group => new CurrencyTotal(
                group.Key,
                group.Sum(item => item.Price!.Value),
                group.Count(item => item.IsEstimate)))
            .OrderBy(total => total.Currency)
            .ToList();
}
