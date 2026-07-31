namespace Hold.Data;

/// <summary>
/// An account. Deliberately not ASP.NET Core Identity: this app needs an identifier to scope
/// queries by and a name to show in the topbar, and Identity would bring seven tables, a
/// password store nothing writes to, and a UI framework to fight with the design tokens.
/// </summary>
public class User
{
    /// <summary>
    /// A GUID rather than an incrementing key, because this value is written into
    /// <see cref="WishList.OwnerId"/> — a 64-character string column that predates accounts.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>
    /// Google's <c>sub</c> claim, and the only thing we match an incoming login against.
    /// Not the email: a Google account's address can change, and matching on one would either
    /// orphan somebody's lists or hand them to whoever inherited the old address.
    /// </summary>
    public required string GoogleSubject { get; set; }

    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
