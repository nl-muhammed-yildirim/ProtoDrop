namespace wa.domain;

/// <summary>A suppressed email (per address + sender). TA-3.2: dbo.EmailSuppression.</summary>
public class EmailSuppression
{
    public Guid Id { get; set; }
    public string Address { get; set; } = null!;
    /// <summary>NULL = suppress from any sender (UQ_ES allows one NULL per address in SQL).</summary>
    public string? SenderEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
