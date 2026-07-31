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
            item.Property(entity => entity.Url).HasMaxLength(2000);
            item.Property(entity => entity.Title).HasMaxLength(300).IsRequired();
            item.Property(entity => entity.Brand).HasMaxLength(120);
            item.Property(entity => entity.ImageUrl).HasMaxLength(2000);
            item.Property(entity => entity.Currency).HasMaxLength(3).IsRequired();
            item.Property(entity => entity.Note).HasMaxLength(1000);

            item.Property(entity => entity.Category).HasConversion<string>().HasMaxLength(20);
            item.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(20);

            item.Property(entity => entity.Price).HasPrecision(18, 2);
            item.Property(entity => entity.LatestPrice).HasPrecision(18, 2);

            item.Ignore(entity => entity.ReadyAt);

            item.HasIndex(entity => entity.Status);
        });

        modelBuilder.Entity<Settings>(settings =>
        {
            settings.HasKey(entity => entity.OwnerId);
            settings.Property(entity => entity.OwnerId).HasMaxLength(64).ValueGeneratedNever();

            settings.Property(entity => entity.DefaultWaitDays).HasDefaultValue(30);
            settings.Property(entity => entity.PreferredCurrency).HasMaxLength(3).HasDefaultValue("USD");
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(entity => entity.Id).HasMaxLength(64).ValueGeneratedNever();
            user.Property(entity => entity.GoogleSubject).HasMaxLength(128).IsRequired();
            user.Property(entity => entity.Email).HasMaxLength(320).IsRequired();
            user.Property(entity => entity.DisplayName).HasMaxLength(120);

            user.HasIndex(entity => entity.GoogleSubject).IsUnique();
        });
    }
}
