namespace wa.domain;

public class EmailSuppression
{
    public Guid Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}