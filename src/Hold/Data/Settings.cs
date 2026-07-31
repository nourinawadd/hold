namespace Hold.Data;

/// <summary>
/// One row per account. This was a single global row keyed to a constant, guarded by a check
/// constraint, back when the app had one user — see the Accounts migration for the change.
/// </summary>
public class Settings
{
    /// <summary>
    /// The <see cref="User.Id"/> these settings belong to, and the primary key. There is no
    /// separate surrogate id: an account has exactly one settings row, and making the owner
    /// the key means the database enforces that rather than the app remembering to.
    /// </summary>
    public required string OwnerId { get; set; }

    public int DefaultWaitDays { get; set; } = 30;

    public string PreferredCurrency { get; set; } = "USD";
}
