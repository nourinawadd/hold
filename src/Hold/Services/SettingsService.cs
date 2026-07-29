using Hold.Data;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public sealed class SettingsService(IDbContextFactory<HoldDbContext> factory)
{
    public async Task<Settings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == Settings.SingletonId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        // A database created outside the dev seed path still needs its one row.
        settings = new Settings { Id = Settings.SingletonId };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);

        return settings;
    }

    public async Task SaveAsync(
        int defaultWaitDays,
        string preferredCurrency,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var settings = await db.Settings
            .SingleOrDefaultAsync(row => row.Id == Settings.SingletonId, cancellationToken);

        if (settings is null)
        {
            settings = new Settings { Id = Settings.SingletonId };
            db.Settings.Add(settings);
        }

        settings.DefaultWaitDays = defaultWaitDays;
        settings.PreferredCurrency = preferredCurrency;

        await db.SaveChangesAsync(cancellationToken);
    }
}
