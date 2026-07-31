using System.Security.Claims;
using Hold.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Hold.Services;

/// <summary>
/// Turns a Google login into an account in this database, and stamps the resulting principal
/// with our own identifier. Runs once per sign-in, before the cookie is written.
/// </summary>
public static class GoogleSignIn
{
    public static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        var principal = context.Principal;

        // Google's stable identifier. Arrives as NameIdentifier from the handler.
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

        // The identifier every owner-scoped query runs on. Added to the principal that is
        // about to become the cookie, so no later request has to look the account up again.
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(CurrentUser.OwnerIdClaim, user.Id));
    }

    /// <summary>
    /// Adopts the rows made before accounts existed, which carry <see cref="WishList.UnclaimedOwnerId"/>.
    ///
    /// <para>
    /// The condition is "this is the only account", not "this account is new" — so a failure
    /// partway through is repaired by the same person signing in again, rather than stranding
    /// the data forever. Once a second account exists nothing is ever adopted again.
    /// </para>
    ///
    /// <para>
    /// Signup is open, so whoever registers first inherits these rows. That was a deliberate
    /// choice; the mitigation is operational — sign in yourself before sharing the link.
    /// </para>
    /// </summary>
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

        // Two statements rather than a transaction: the context is configured with
        // EnableRetryOnFailure, and its execution strategy refuses to manage a transaction it
        // did not start. Each ExecuteUpdate is atomic on its own, and the guard above makes
        // running them again harmless.
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
