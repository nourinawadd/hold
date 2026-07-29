namespace Hold.Data;

public class Settings
{
    /// <summary>
    /// Settings is a single row. The key is fixed to this value and a check constraint in
    /// the database rejects any other, so the invariant holds even against a hand-written
    /// INSERT in a database browser.
    /// </summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public int DefaultWaitDays { get; set; } = 30;

    public string PreferredCurrency { get; set; } = "USD";
}
