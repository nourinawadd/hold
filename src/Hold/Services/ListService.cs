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
    IReadOnlyList<CurrencyTotal> Totals);

public sealed class ListService(IDbContextFactory<HoldDbContext> factory)
{
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
                Prices = list.Items
                    .Select(item => new { item.Price, item.Currency })
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
                row.Prices.Count,
                row.UpdatedAt,
                row.Prices
                    .Where(entry => entry.Price.HasValue)
                    .GroupBy(entry => entry.Currency)
                    .Select(group => new CurrencyTotal(group.Key, group.Sum(entry => entry.Price!.Value)))
                    .OrderBy(total => total.Currency)
                    .ToList()))
            .ToList();
    }
}
