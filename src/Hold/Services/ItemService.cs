using System.Globalization;
using Hold.Data;
using Hold.Scraping;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public sealed record ItemDraft(
    string? Url,
    string Title,
    string? Brand,
    string? ImageUrl,
    decimal? Price,
    string Currency,
    Category Category,
    int WaitDays,
    string? Note,
    bool PriceIsEstimate = false);

public sealed record ItemFilter(Category? Category = null, string? Search = null);

public sealed record PriceCheck(decimal Saved, decimal Latest, string Currency)
{
    public bool Dropped => Latest < Saved;

    public bool Rose => Latest > Saved;

    public int PercentChange =>
        Saved <= 0 ? 0 : (int)Math.Round(Math.Abs(Latest - Saved) / Saved * 100m, MidpointRounding.AwayFromZero);

    public string Describe() => (Dropped, Rose) switch
    {
        (true, _) => $"Down {PercentChange}% since you saved it.",
        (_, true) => $"Up {PercentChange}% since you saved it.",
        _ => "The same as when you saved it.",
    };
}

public sealed record PriceCheckResult(PriceCheck? Check, string Message);

public sealed class ItemService(
    IDbContextFactory<HoldDbContext> factory,
    TimeProvider time,
    CurrentUser user,
    ProductScraper scraper)
{
    public const int UrlMaxLength = 2000;
    public const int TitleMaxLength = 300;
    public const int BrandMaxLength = 120;
    public const int NoteMaxLength = 1000;

    public const int MinWaitDays = 1;
    public const int MaxWaitDays = 3650;

    public static string? DescribeProblem(ItemDraft draft)
    {
        var url = draft.Url?.Trim();

        if (!string.IsNullOrEmpty(url))
        {
            if (url.Length > UrlMaxLength)
            {
                return $"That link is {url.Length} characters. Keep it under {UrlMaxLength}.";
            }

            if (!IsWebAddress(url))
            {
                return "That link should start with http:// or https://.";
            }
        }

        var title = draft.Title?.Trim();

        if (string.IsNullOrEmpty(title))
        {
            return "An item needs a name.";
        }

        if (title.Length > TitleMaxLength)
        {
            return $"That name is {title.Length} characters. Keep it under {TitleMaxLength}.";
        }

        if (draft.Brand?.Trim().Length > BrandMaxLength)
        {
            return $"That brand is longer than {BrandMaxLength} characters.";
        }

        var imageUrl = draft.ImageUrl?.Trim();

        if (!string.IsNullOrEmpty(imageUrl))
        {
            if (imageUrl.Length > UrlMaxLength)
            {
                return $"That image link is longer than {UrlMaxLength} characters.";
            }

            if (!IsWebAddress(imageUrl))
            {
                return "That image link should start with http:// or https://.";
            }
        }

        var currency = draft.Currency?.Trim();

        if (string.IsNullOrEmpty(currency) || currency.Length != 3 || !currency.All(char.IsLetter))
        {
            return "Currency should be a three-letter code, like USD.";
        }

        if (draft.Price is < 0)
        {
            return "A price cannot be negative.";
        }

        if (draft.WaitDays is < MinWaitDays or > MaxWaitDays)
        {
            return $"Choose a wait between {MinWaitDays} and {MaxWaitDays} days.";
        }

        if (draft.Note?.Trim().Length > NoteMaxLength)
        {
            return $"That note is longer than {NoteMaxLength} characters.";
        }

        return null;
    }

    public async Task<IReadOnlyList<Item>> GetForListAsync(
        int listId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var items = await db.Items
            .AsNoTracking()
            .Where(item => item.WishListId == listId && item.WishList.OwnerId == owner)
            .ToListAsync(cancellationToken);

        return Sort(items, time.GetUtcNow());
    }

    public async Task<IReadOnlyList<Item>> GetAllAsync(
        ItemFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var items = await db.Items
            .AsNoTracking()
            .Include(item => item.WishList)
            .Where(item => item.WishList.OwnerId == owner)
            .ToListAsync(cancellationToken);

        return Sort(
            items.Where(item =>
                (filter.Category is null || item.Category == filter.Category)
                && Matches(item, filter.Search)),
            time.GetUtcNow());
    }

    public static bool Matches(Item item, string? search)
    {
        var term = search?.Trim();

        if (string.IsNullOrEmpty(term))
        {
            return true;
        }

        return Contains(item.Title, term)
            || Contains(item.Brand, term)
            || Contains(item.WishList?.Name, term);
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null
        && CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    public static List<Item> Sort(IEnumerable<Item> items, DateTimeOffset now) =>
        items
            .OrderBy(item => item.Status == ItemStatus.Waiting ? 0 : 1)
            .ThenBy(item => item.IsReady(now) ? 0 : 1)
            .ThenBy(item => item.SavedAt)
            .ToList();

    public async Task<int> AddAsync(
        int listId,
        ItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var problem = DescribeProblem(draft);

        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(draft));
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == listId && row.OwnerId == owner,
            cancellationToken);

        if (list is null)
        {
            throw new InvalidOperationException($"List {listId} does not exist.");
        }

        var now = time.GetUtcNow();
        var url = Clean(draft.Url);

        var item = new Item
        {
            WishListId = listId,
            Url = url,
            Title = draft.Title.Trim(),
            Brand = Clean(draft.Brand),
            ImageUrl = Clean(draft.ImageUrl),
            Price = draft.Price,
            PriceIsEstimate = draft.PriceIsEstimate || EstimatedPrice.LikelyEstimate(url),
            Currency = draft.Currency.Trim().ToUpperInvariant(),
            Category = draft.Category,
            WaitDays = draft.WaitDays,
            SavedAt = now,
            Status = ItemStatus.Waiting,
            Note = Clean(draft.Note),
        };

        db.Items.Add(item);

        list.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return item.Id;
    }

    /// <summary>
    /// Whether an edit invalidates a stored re-check. The card renders "Now 45.00 USD" whenever
    /// <see cref="Item.LatestPrice"/> differs from <see cref="Item.Price"/>, so once the saved price,
    /// its currency, or the link it was read from changes, that line compares against a number that
    /// no longer exists.
    /// </summary>
    public static bool RetiresLatestPrice(Item item, ItemDraft draft) =>
        item.Price != draft.Price
        || !string.Equals(item.Currency, draft.Currency?.Trim(), StringComparison.OrdinalIgnoreCase)
        || item.Url != Clean(draft.Url);

    public async Task<bool> UpdateAsync(
        int itemId,
        ItemDraft draft,
        CancellationToken cancellationToken = default)
    {
        var problem = DescribeProblem(draft);

        if (problem is not null)
        {
            throw new ArgumentException(problem, nameof(draft));
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var item = await db.Items
            .Include(entity => entity.WishList)
            .SingleOrDefaultAsync(
                entity => entity.Id == itemId && entity.WishList.OwnerId == owner,
                cancellationToken);

        if (item is null)
        {
            return false;
        }

        if (RetiresLatestPrice(item, draft))
        {
            item.LatestPrice = null;
            item.LatestPriceAt = null;
        }

        item.Url = Clean(draft.Url);
        item.Title = draft.Title.Trim();
        item.Brand = Clean(draft.Brand);
        item.ImageUrl = Clean(draft.ImageUrl);
        item.Price = draft.Price;
        item.Currency = draft.Currency.Trim().ToUpperInvariant();
        item.Category = draft.Category;
        item.WaitDays = draft.WaitDays;
        item.Note = Clean(draft.Note);

        // Taken verbatim rather than OR-ed with EstimatedPrice.LikelyEstimate as AddAsync does. On the
        // add path that inference is a default for a value nobody has seen yet; here the stored flag is
        // a decision already made, and re-inferring would re-tick an item the user deliberately
        // un-ticked every time they fixed a typo.
        item.PriceIsEstimate = draft.PriceIsEstimate;

        // SavedAt, Status and ClosedAt are deliberately untouched. SavedAt is the origin of the day
        // count, the progress hairline and the sort — correcting a name must not change how long
        // something has been wanted.
        item.WishList.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> DeleteAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var item = await db.Items
            .Include(entity => entity.WishList)
            .SingleOrDefaultAsync(
                entity => entity.Id == itemId && entity.WishList.OwnerId == owner,
                cancellationToken);

        if (item is null)
        {
            return false;
        }

        db.Items.Remove(item);

        item.WishList.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> SetStatusAsync(
        int itemId,
        ItemStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var item = await db.Items
            .Include(entity => entity.WishList)
            .SingleOrDefaultAsync(
                entity => entity.Id == itemId && entity.WishList.OwnerId == owner,
                cancellationToken);

        if (item is null)
        {
            return false;
        }

        var now = time.GetUtcNow();

        item.Status = status;

        item.ClosedAt = status == ItemStatus.Waiting ? null : now;

        item.WishList.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<PriceCheckResult> RecheckPriceAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var owner = await user.RequireIdAsync();

        var item = await db.Items
            .Include(entity => entity.WishList)
            .SingleOrDefaultAsync(
                entity => entity.Id == itemId && entity.WishList.OwnerId == owner,
                cancellationToken);

        if (item is null)
        {
            return new PriceCheckResult(null, "That item is no longer here.");
        }

        if (item.Url is null)
        {
            return new PriceCheckResult(null, "There is no link to read.");
        }

        if (item.PriceIsEstimate)
        {
            return new PriceCheckResult(null, "This price is your estimate, so there is nothing to re-read.");
        }

        if (item.Price is not { } saved)
        {
            return new PriceCheckResult(null, "There is no saved price to compare against.");
        }

        var outcome = await scraper.ReadAsync(item.Url, null, cancellationToken);

        if (outcome.Info.Price is not { } found
            || !outcome.Has(ProductField.Price)
            || outcome.IsUnverified(ProductField.Price))
        {
            return new PriceCheckResult(null, "The price could not be read just now.");
        }

        if (!string.Equals(outcome.Info.Currency, item.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return new PriceCheckResult(
                null,
                $"The shop is quoting {outcome.Info.Currency ?? "another currency"} now, not {item.Currency}.");
        }

        item.LatestPrice = found;
        item.LatestPriceAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        var check = new PriceCheck(saved, found, item.Currency);

        return new PriceCheckResult(check, check.Describe());
    }

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
