using wa.domain.Privacy;

namespace wa.domain;

/// <summary>An email recipient of a transfer. TA-3.2: dbo.EmailRecipient.</summary>
public class EmailRecipient
{
    /// <summary>Primary key of the recipient row.</summary>
    public Guid Id { get; set; }
    /// <summary>The transfer the recipient was invited to.</summary>
    public Guid TransferId { get; set; }
    /// <summary>Recipient email address, normalized lowercase; PII (TA-9.4).</summary>
    [Pii]
    public string Address { get; set; } = null!;
    /// <summary>Set when the recipient was notified (TA-6.7 f-email).</summary>
    public DateTime? NotifiedAtUtc { get; set; }

    /// <summary>T-006-04: Preferred language for email rendering (TA-3.2). Null/empty → English fallback.</summary>
    public string? PreferredLanguage { get; set; }
}
