using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public sealed class SettingsService(IDbContextFactory<HoldDbContext> factory, CurrentUser user)
{
    public async Task<Settings> GetAsync(CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.OwnerId == owner, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        // A new account has no row yet, and creating one at signup would mean a second thing
        // the sign-in path can fail at. Written on first read instead, with the spec defaults
        // supplied by the database.
        settings = new Settings { OwnerId = owner };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    public async Task SaveAsync(
        int defaultWaitDays,
        string preferredCurrency,
        CancellationToken cancellationToken = default)
    {
        var owner = await user.RequireIdAsync();

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .SingleOrDefaultAsync(row => row.OwnerId == owner, cancellationToken);

        if (settings is null)
        {
            settings = new Settings { OwnerId = owner };
            db.Settings.Add(settings);
        }

        settings.DefaultWaitDays = defaultWaitDays;
        settings.PreferredCurrency = preferredCurrency;

        await db.SaveChangesAsync(cancellationToken);
    }
}
