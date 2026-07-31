using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Hold.Services;

public sealed class CurrentUser(AuthenticationStateProvider authentication)
{
    public const string OwnerIdClaim = "hold:owner";

    public async Task<string?> IdAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();

        return state.User.Identity?.IsAuthenticated is true
            ? state.User.FindFirst(OwnerIdClaim)?.Value
            : null;
    }

    public async Task<string> RequireIdAsync() =>
        await IdAsync()
        ?? throw new InvalidOperationException(
            "No signed-in account. An owner-scoped service was reached from a page that is "
            + "not behind [Authorize].");
}
