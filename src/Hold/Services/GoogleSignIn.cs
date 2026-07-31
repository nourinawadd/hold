using System.Security.Claims;
using Hold.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

public static class GoogleSignIn
{
    public static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var principal = context.Principal;

        var subject = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (principal is null || string.IsNullOrEmpty(subject))
        {
            context.Fail("Google returned no subject claim.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var factory = services.GetRequiredService<IDbContextFactory<HoldDbContext>>();
        var time = services.GetRequiredService<TimeProvider>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GoogleSignIn));

        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);

        var user = await db.Users.SingleOrDefaultAsync(
            row => row.GoogleSubject == subject,
            context.HttpContext.RequestAborted);

        if (user is null)
        {
            user = new User
            {
                GoogleSubject = subject,
                Email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
                DisplayName = principal.FindFirst(ClaimTypes.Name)?.Value,
                CreatedAt = time.GetUtcNow(),
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(context.HttpContext.RequestAborted);

            log.LogInformation("Created account {UserId} for a new Google sign-in.", user.Id);
        }

        await ClaimUnownedRowsAsync(db, user, log, context.HttpContext.RequestAborted);

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(CurrentUser.OwnerIdClaim, user.Id));
    }

    private static async Task ClaimUnownedRowsAsync(
        HoldDbContext db,
        User user,
        ILogger log,
        CancellationToken cancellationToken)
    {
        if (await db.Users.CountAsync(cancellationToken) != 1)
        {
            return;
        }

        var lists = await db.WishLists
            .Where(row => row.OwnerId == WishList.UnclaimedOwnerId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.OwnerId, user.Id),
                cancellationToken);

        var settings = await db.Settings
            .Where(row => row.OwnerId == WishList.UnclaimedOwnerId)
            .ExecuteUpdateAsync(
                update => update.SetProperty(row => row.OwnerId, user.Id),
                cancellationToken);

        if (lists > 0 || settings > 0)
        {
            log.LogInformation(
                "Adopted {Lists} unclaimed list(s) and {Settings} settings row(s) into account {UserId}.",
                lists,
                settings,
                user.Id);
        }
    }
}
