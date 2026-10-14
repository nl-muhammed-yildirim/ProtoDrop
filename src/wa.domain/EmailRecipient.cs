namespace wa.domain;

/// <summary>An email recipient of a transfer. TA-3.2: dbo.EmailRecipient.</summary>
public class EmailRecipient
{
    public Guid Id { get; set; }
    public Guid TransferId { get; set; }
    public string Address { get; set; } = null!;
    /// <summary>Set when the recipient was notified (TA-6.7 f-email).</summary>
    public DateTime? NotifiedAtUtc { get; set; }
}
