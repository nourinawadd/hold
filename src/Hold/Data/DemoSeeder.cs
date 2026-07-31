using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

public static class DemoSeeder
{
    public static async Task SeedAsync(
        HoldDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.WishLists
            .AnyAsync(list => list.ShareToken == WishList.DemoShareToken, cancellationToken);

        if (exists)
        {
            return;
        }

        db.WishLists.Add(Build(timeProvider.GetUtcNow()));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static WishList Build(DateTimeOffset now) => new()
    {
        OwnerId = WishList.DemoOwnerId,
        ShareToken = WishList.DemoShareToken,
        Name = "Wishlist",
        Description = "An example of what Hold looks like in use.",
        CreatedAt = now.AddDays(-96),
        UpdatedAt = now.AddDays(-1),
        Items =
        [
            Item(
                "https://shop.doen.com/products/sylvie-coat",
                "Sylvie Coat",
                "Dôen",
                890.00m,
                Category.Outerwear,
                waitDays: 30,
                savedAt: now.AddDays(-96)),
            Item(
                "https://margauxny.com/products/the-classic-ballet-flat",
                "The Classic Ballet Flat",
                "Margaux",
                264.00m,
                Category.Shoes,
                waitDays: 45,
                savedAt: now.AddDays(-61)),
            Item(
                "https://hasamiporcelain.com/products/mug-tall",
                "Tall Mug, Set of Two",
                "Hasami Porcelain",
                85.00m,
                Category.Home,
                waitDays: 30,
                savedAt: now.AddDays(-34)),
            Item(
                null,
                "Floating oak shelf for the hallway",
                null,
                60.00m,
                Category.Projects,
                waitDays: 30,
                savedAt: now.AddDays(-21),
                priceIsEstimate: true),
            Item(
                "https://mejuri.com/products/bold-hoops",
                "Bold Hoops",
                "Mejuri",
                50.00m,
                Category.Jewellery,
                waitDays: 60,
                savedAt: now.AddDays(-9)),
            Item(
                "https://naadam.co/products/the-essential-scarf",
                "The Essential Scarf",
                "Naadam",
                120.00m,
                Category.Other,
                waitDays: 21,
                savedAt: now.AddDays(-3)),
            Item(
                "https://hedleyandbennett.com/products/the-essential-apron",
                "The Essential Apron",
                "Hedley & Bennett",
                65.00m,
                Category.Home,
                waitDays: 30,
                savedAt: now.AddDays(-47),
                status: ItemStatus.LetGo,
                closedAt: now.AddDays(-16)),
            Item(
                "https://ferncandles.com/products/beeswax-taper",
                "Beeswax Taper, Pair",
                "Fern",
                40.00m,
                Category.Home,
                waitDays: 21,
                savedAt: now.AddDays(-58),
                status: ItemStatus.Bought,
                closedAt: now.AddDays(-30)),
        ],
    };

    private static Item Item(
        string? url,
        string title,
        string? brand,
        decimal price,
        Category category,
        int waitDays,
        DateTimeOffset savedAt,
        bool priceIsEstimate = false,
        ItemStatus status = ItemStatus.Waiting,
        DateTimeOffset? closedAt = null) => new()
        {
            Url = url,
            Title = title,
            Brand = brand,
            Price = price,
            PriceIsEstimate = priceIsEstimate,
            Currency = "USD",
            Category = category,
            WaitDays = waitDays,
            SavedAt = savedAt,
            Status = status,
            ClosedAt = closedAt,
        };
}
