using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

public static class DevDataSeeder
{
    public static async Task SeedAsync(
        HoldDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        var pending = false;

        if (!await db.Settings.AnyAsync(cancellationToken))
        {
            db.Settings.Add(new Settings { OwnerId = WishList.UnclaimedOwnerId });
            pending = true;
        }

        var seeded = await db.WishLists
            .AnyAsync(list => list.OwnerId != WishList.DemoOwnerId, cancellationToken);

        if (!seeded)
        {
            db.WishLists.AddRange(BuildLists(timeProvider.GetUtcNow()));
            pending = true;
        }

        if (pending)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static IEnumerable<WishList> BuildLists(DateTimeOffset now)
    {
        yield return new WishList
        {
            OwnerId = WishList.UnclaimedOwnerId,
            Name = "Wishlist",
            Description = "The long game.",
            BudgetAmount = 1500.00m,
            BudgetCurrency = "USD",
            CreatedAt = now.AddDays(-40),
            UpdatedAt = now.AddHours(-6),
            Items =
            [
                NewItem(
                    "https://shop.doen.com/products/sylvie-coat",
                    "Sylvie Coat",
                    "Dôen",
                    890.00m,
                    Category.Outerwear,
                    waitDays: 30,
                    savedAt: now.AddDays(-40)),
                NewItem(
                    "https://margauxny.com/products/the-classic-ballet-flat",
                    "The Classic Ballet Flat",
                    "Margaux",
                    264.00m,
                    Category.Shoes,
                    waitDays: 30,
                    savedAt: now.AddDays(-12)),
                NewItem(
                    "https://mejuri.com/products/bold-hoops",
                    "Bold Hoops",
                    "Mejuri",
                    50.00m,
                    Category.Jewellery,
                    waitDays: 45,
                    savedAt: now.AddDays(-2)),
            ],
        };

        yield return new WishList
        {
            OwnerId = WishList.UnclaimedOwnerId,
            Name = "Gifts",
            CreatedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-2),
            Items =
            [
                NewItem(
                    "https://naadam.co/products/the-essential-scarf",
                    "The Essential Scarf",
                    "Naadam",
                    120.00m,
                    Category.Other,
                    waitDays: 14,
                    savedAt: now.AddDays(-18)),
                NewItem(
                    "https://hasamiporcelain.com/products/mug-tall",
                    "Tall Mug, Set of Two",
                    "Hasami Porcelain",
                    85.00m,
                    Category.Home,
                    waitDays: 30,
                    savedAt: now.AddDays(-9)),
                NewItem(
                    "https://hedleyandbennett.com/products/the-essential-apron",
                    "The Essential Apron",
                    "Hedley & Bennett",
                    65.00m,
                    Category.Home,
                    waitDays: 30,
                    savedAt: now.AddDays(-5)),
                NewItem(
                    "https://ferncandles.com/products/beeswax-taper",
                    "Beeswax Taper, Pair",
                    "Fern",
                    40.00m,
                    Category.Home,
                    waitDays: 21,
                    savedAt: now.AddDays(-2)),
            ],
        };

        yield return new WishList
        {
            OwnerId = WishList.UnclaimedOwnerId,
            Name = "Travel",
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-11),
        };
    }

    private static Item NewItem(
        string url,
        string title,
        string brand,
        decimal price,
        Category category,
        int waitDays,
        DateTimeOffset savedAt) => new()
        {
            Url = url,
            Title = title,
            Brand = brand,
            Price = price,
            Currency = "USD",
            Category = category,
            WaitDays = waitDays,
            SavedAt = savedAt,
            Status = ItemStatus.Waiting,
        };
}
