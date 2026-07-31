namespace Hold.Data;

public class Settings
{
    public required string OwnerId { get; set; }

    public int DefaultWaitDays { get; set; } = 30;

    public string PreferredCurrency { get; set; } = "USD";
}
