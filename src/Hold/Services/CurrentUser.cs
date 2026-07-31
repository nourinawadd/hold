using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hold.Services;

/// <summary>
/// Who is asking. Every owner-scoped query goes through this rather than reading claims
/// directly, so the one place that decides "which account is this" is a file rather than a
/// pattern repeated at six call sites.
/// </summary>
public sealed class CurrentUser(AuthenticationStateProvider authentication)
{
    /// <summary>
    /// Carries <see cref="Data.User.Id"/>. A private claim type rather than NameIdentifier,
    /// which the Google handler already fills with Google's own subject — two different
    /// identifiers that must not be confused, since only one of them is a foreign key here.
    /// </summary>
    public const string OwnerIdClaim = "hold:owner";

    public async Task<string?> IdAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();

        return state.User.Identity?.IsAuthenticated is true
            ? state.User.FindFirst(OwnerIdClaim)?.Value
            : null;
    }

    /// <summary>
    /// For the owner-scoped services, which are only ever reached through a page carrying
    /// [Authorize]. Throwing here means a route escaped that attribute — a bug worth a stack
    /// trace, not a silent query against an empty owner that would return someone's lists.
    /// </summary>
    public async Task<string> RequireIdAsync() =>
        await IdAsync()
        ?? throw new InvalidOperationException(
            "No signed-in account. An owner-scoped service was reached from a page that is "
            + "not behind [Authorize].");
}
