namespace wa.domain;

/// <summary>Client idempotency key (TA-4.1.5). TA-3.2: dbo.IdempotencyKey.</summary>
public class IdempotencyKey
{
    /// <summary>The client-supplied unique opaque string — primary key (VARCHAR(128)).</summary>
    public string Key { get; set; } = null!;
    /// <summary>Gets or sets the result JSON of the idempotency key.</summary>
    public string? ResultJson { get; set; }
    /// <summary>Gets or sets the transfer ID of the idempotency key.</summary>
    public Guid? TransferId { get; set; }
    /// <summary>Gets or sets the created at UTC of the idempotency key.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>24 h TTL (TA-4.1.5).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>HTTP status code to replay on idempotent hit (T-002-02).</summary>
    public int StatusCode { get; set; }
}
