namespace wa.domain;

public class EmailRecipient
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public string Address { get; set; } = string.Empty;
    public DateTime? NotifiedAtUtc { get; set; }
}
