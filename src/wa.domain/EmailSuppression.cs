using wa.domain.Privacy;

namespace wa.domain;

/// <summary>A suppressed email (per address + sender). TA-3.2: dbo.EmailSuppression.</summary>
public class EmailSuppression
{
    /// <summary>Primary key of the suppression row.</summary>
    public Guid Id { get; set; }
    /// <summary>Recipient address that opted out of mail; PII (TA-9.4).</summary>
    [Pii]
    public string Address { get; set; } = null!;
    /// <summary>NULL = suppress from any sender (one per address; enforced app-side — UQ_ES allows multiple NULLs in SQL Server).</summary>
    [Pii]
    public string? SenderEmail { get; set; }
    /// <summary>UTC instant the recipient unsubscribed.</summary>
    public DateTime CreatedAtUtc { get; set; }
}
