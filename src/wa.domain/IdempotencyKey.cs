namespace wa.domain;

/// <summary>Client idempotency key (TA-4.1.5). TA-3.2: dbo.IdempotencyKey.</summary>
public class IdempotencyKey
{
    /// <summary>The client-supplied unique opaque string — primary key (VARCHAR(128)).</summary>
    public string Key { get; set; } = null!;
    public string? ResultJson { get; set; }
    public Guid? TransferId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>24 h TTL (TA-4.1.5).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>HTTP status code to replay on idempotent hit (T-002-02).</summary>
    public int StatusCode { get; set; }
}
