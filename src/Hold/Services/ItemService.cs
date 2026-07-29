using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

/// <summary>
/// What the add form collects. Phase 5 fills one of these from a scrape instead of by
/// hand, which is why it is separate from the entity.
/// </summary>
public sealed record ItemDraft(
    string Url,
    string Title,
    string? Brand,
    string? ImageUrl,
    decimal? Price,
    string Currency,
    Category Category,
    int WaitDays,
    string? Note);

public sealed class ItemService(IDbContextFactory<HoldDbContext> factory, TimeProvider time)
{
    // Mirrors the column limits in HoldDbContext.OnModelCreating.
    public const int UrlMaxLength = 2000;
    public const int TitleMaxLength = 300;
    public const int BrandMaxLength = 120;
    public const int NoteMaxLength = 1000;

    // A zero-day wait is not a wait, and ten years is past the point of usefulness.
    public const int MinWaitDays = 1;
    public const int MaxWaitDays = 3650;

    /// <summary>
    /// The one place the item rules live. The form calls this to show a message; AddAsync
    /// calls it again so a bad draft cannot reach the database by another route.
    /// </summary>
    public static string? DescribeProblem(ItemDraft draft)
    {
        var url = draft.Url?.Trim();

        if (string.IsNullOrEmpty(url))
        {
            return "An item needs a link.";
        }

        if (url.Length > UrlMaxLength)
        {
            return $"That link is {url.Length} characters. Keep it under {UrlMaxLength}.";
        }

        if (!IsWebAddress(url))
        {
            return "That link should start with http:// or https://.";
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

        // Three letters, because the column is char(3) and every currency code is.
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

    /// <summary>
    /// Returns entities rather than a projection: Item already carries ReadyAt, IsReady,
    /// and DaysWaited, and projecting would mean writing that arithmetic a second time.
    /// </summary>
    public async Task<IReadOnlyList<Item>> GetForListAsync(
        int listId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // No OrderBy in the query: SavedAt is a DateTimeOffset and Price is TEXT, neither
        // of which SQLite can sort meaningfully. Materialise first, then sort below.
        var items = await db.Items
            .AsNoTracking()
            .Where(item => item.WishListId == listId)
            .ToListAsync(cancellationToken);

        return Sort(items);
    }

    /// <summary>
    /// Waiting items first, then closed. Within each group, oldest first — that is
    /// days-waited descending, and the longest-held thing is the most interesting.
    /// Phase 6 inserts a ready-first tier above this.
    /// </summary>
    public static List<Item> Sort(IEnumerable<Item> items) =>
        items
            .OrderBy(item => item.Status == ItemStatus.Waiting ? 0 : 1)
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

        var list = await db.WishLists.SingleOrDefaultAsync(
            row => row.Id == listId && row.OwnerId == WishList.DefaultOwnerId,
            cancellationToken);

        if (list is null)
        {
            throw new InvalidOperationException($"List {listId} does not exist.");
        }

        var now = time.GetUtcNow();

        var item = new Item
        {
            WishListId = listId,
            Url = draft.Url.Trim(),
            Title = draft.Title.Trim(),
            Brand = Clean(draft.Brand),
            ImageUrl = Clean(draft.ImageUrl),
            Price = draft.Price,
            Currency = draft.Currency.Trim().ToUpperInvariant(),
            Category = draft.Category,
            WaitDays = draft.WaitDays,
            SavedAt = now,
            Status = ItemStatus.Waiting,
            Note = Clean(draft.Note),
        };

        db.Items.Add(item);

        // Adding to a list is activity on that list, so its card carries a fresh time.
        list.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return item.Id;
    }

    /// <summary>Returns false when the item no longer exists — a stale card, not an error.</summary>
    public async Task<bool> SetStatusAsync(
        int itemId,
        ItemStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var item = await db.Items
            .Include(entity => entity.WishList)
            .SingleOrDefaultAsync(entity => entity.Id == itemId, cancellationToken);

        if (item is null)
        {
            return false;
        }

        var now = time.GetUtcNow();

        item.Status = status;

        // Reopening clears the closing date rather than leaving a stale one behind.
        item.ClosedAt = status == ItemStatus.Waiting ? null : now;

        item.WishList.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return true;
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
