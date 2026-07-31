namespace Hold.Data;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public required string GoogleSubject { get; set; }

    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
