namespace SaveNEIN.Shared;

public class Legislator
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // City Council, State House
    public string? District { get; set; }
    public string? Party { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
