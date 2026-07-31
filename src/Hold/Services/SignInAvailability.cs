namespace Hold.Services;

/// <summary>
/// Whether Google sign-in is configured on this deployment.
///
/// <para>
/// Exists so the sign-in page can say so plainly rather than offering a link that fails. The
/// Google handler validates its options on every request rather than at startup, so an empty
/// ClientId would otherwise turn every page — including /health — into a 500.
/// </para>
/// </summary>
public sealed record SignInAvailability(bool GoogleConfigured);
