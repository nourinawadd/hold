using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

public class HoldDbContext(DbContextOptions<HoldDbContext> options) : DbContext(options)
{
    public DbSet<WishList> WishLists => Set<WishList>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Settings> Settings => Set<Settings>();

    public DbSet<User> Users => Set<User>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Registered as a convention so it covers DateTimeOffset and DateTimeOffset? alike,
        // and so a property added in a later phase cannot forget it.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WishList>(list =>
        {
            list.Property(wishList => wishList.Name).HasMaxLength(100).IsRequired();
            list.Property(wishList => wishList.Description).HasMaxLength(500);
            list.Property(wishList => wishList.BudgetAmount).HasPrecision(18, 2);
            list.Property(wishList => wishList.BudgetCurrency).HasMaxLength(3);
            list.Property(wishList => wishList.OwnerId).HasMaxLength(64).IsRequired();

            list.HasIndex(wishList => wishList.OwnerId);

            // Unique, and filtered so it applies only to shared lists. Without the filter every
            // unshared row would collide on NULL in a provider that treats nulls as equal, and
            // the index would be uselessly large besides — most lists are never shared.
            list.Property(wishList => wishList.ShareToken).HasMaxLength(64);

            list.HasIndex(wishList => wishList.ShareToken)
                .IsUnique()
                .HasFilter("\"ShareToken\" IS NOT NULL");

            list.HasMany(wishList => wishList.Items)
                .WithOne(item => item.WishList)
                .HasForeignKey(item => item.WishListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Item>(item =>
        {
            item.Property(entity => entity.Url).HasMaxLength(2000).IsRequired();
            item.Property(entity => entity.Title).HasMaxLength(300).IsRequired();
            item.Property(entity => entity.Brand).HasMaxLength(120);
            item.Property(entity => entity.ImageUrl).HasMaxLength(2000);
            item.Property(entity => entity.Currency).HasMaxLength(3).IsRequired();
            item.Property(entity => entity.Note).HasMaxLength(1000);

            // Written as words rather than ordinals, so a row read straight from the database
            // says Outerwear rather than 4.
            item.Property(entity => entity.Category).HasConversion<string>().HasMaxLength(20);
            item.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(20);

            // Money is numeric(18,2), not the arbitrary precision Postgres would otherwise pick.
            // Two decimal places is what shops quote and what the app displays.
            item.Property(entity => entity.Price).HasPrecision(18, 2);

            // Ready is derived on read. Ignored explicitly rather than relying on EF's
            // treatment of get-only properties.
            item.Ignore(entity => entity.ReadyAt);

            item.HasIndex(entity => entity.Status);
        });

        modelBuilder.Entity<Settings>(settings =>
        {
            // The owner is the key. One row per account is now a property of the schema, which
            // is what the old CK_Settings_SingleRow check constraint was doing when there was
            // only ever one account. That constraint is dropped in the Accounts migration.
            settings.HasKey(entity => entity.OwnerId);
            settings.Property(entity => entity.OwnerId).HasMaxLength(64).ValueGeneratedNever();

            // Database-side defaults so a row inserted by hand still gets the spec values.
            // Note this makes 0 unreachable for DefaultWaitDays, which is not a meaningful
            // wait anyway.
            settings.Property(entity => entity.DefaultWaitDays).HasDefaultValue(30);
            settings.Property(entity => entity.PreferredCurrency).HasMaxLength(3).HasDefaultValue("USD");
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(entity => entity.Id).HasMaxLength(64).ValueGeneratedNever();
            user.Property(entity => entity.GoogleSubject).HasMaxLength(128).IsRequired();
            user.Property(entity => entity.Email).HasMaxLength(320).IsRequired();
            user.Property(entity => entity.DisplayName).HasMaxLength(120);

            // The lookup every sign-in performs, and the guarantee that one Google account
            // cannot become two rows if two logins race.
            user.HasIndex(entity => entity.GoogleSubject).IsUnique();
        });
    }
}
