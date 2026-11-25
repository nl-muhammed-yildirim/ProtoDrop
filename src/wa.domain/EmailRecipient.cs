namespace wa.domain;

/// <summary>An email recipient of a transfer. TA-3.2: dbo.EmailRecipient.</summary>
public class EmailRecipient
{
    /// <summary>Gets or sets the ID of the email recipient.</summary>
    public Guid Id { get; set; }
    /// <summary>Gets or sets the transfer ID of the email recipient.</summary>
    public Guid TransferId { get; set; }
    /// <summary>Gets or sets the address of the email recipient.</summary>
    public string Address { get; set; } = null!;
    /// <summary>Set when the recipient was notified (TA-6.7 f-email).</summary>
    public DateTime? NotifiedAtUtc { get; set; }

    /// <summary>T-006-04: Preferred language for email rendering (TA-3.2). Null/empty → English fallback.</summary>
    public string? PreferredLanguage { get; set; }
}
