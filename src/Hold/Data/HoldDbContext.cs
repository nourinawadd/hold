using Microsoft.EntityFrameworkCore;

namespace Hold.Data;

public class HoldDbContext(DbContextOptions<HoldDbContext> options) : DbContext(options)
{
    public DbSet<WishList> WishLists => Set<WishList>();

    public DbSet<Item> Items => Set<Item>();

    public DbSet<Settings> Settings => Set<Settings>();

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
            settings.HasKey(entity => entity.Id);
            settings.Property(entity => entity.Id).ValueGeneratedNever();

            // Database-side defaults so a row inserted by hand still gets the spec values.
            // Note this makes 0 unreachable for DefaultWaitDays, which is not a meaningful
            // wait anyway.
            settings.Property(entity => entity.DefaultWaitDays).HasDefaultValue(30);
            settings.Property(entity => entity.PreferredCurrency).HasMaxLength(3).HasDefaultValue("USD");

            // The single-row guarantee, enforced by the database rather than by the app.
            // Id is quoted deliberately: Postgres folds an unquoted identifier to lower case,
            // and EF creates the column as "Id", so a bare Id = 1 refers to a column that does
            // not exist and the migration fails.
            settings.ToTable(table =>
                table.HasCheckConstraint("CK_Settings_SingleRow", "\"Id\" = 1"));
        });
    }
}
