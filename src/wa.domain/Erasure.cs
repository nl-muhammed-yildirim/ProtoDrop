namespace wa.domain;

/// <summary>
/// US-047-02 (FR-047-5): one account-deletion erasure marker. Table dbo.Erasure (migration AddErasure).
/// Written in the same transaction as the soft delete; the nightly f-gdpr-sweep marks it swept.
/// </summary>
public class Erasure
{
    /// <summary>The deleted account — one row per account (EC-047-3). No FK: a later hard delete must not erase the record.</summary>
    public Guid AppUserId { get; set; }
    /// <summary>UTC instant the account was soft-deleted and the marker written.</summary>
    public DateTime DeletedAtUtc { get; set; }
    /// <summary>Owned transfers re-homed to anonymous at delete time (only knowable then).</summary>
    public int ReHomedTransferCount { get; set; }
    /// <summary>Null = pending; set once by the sweep, never rewritten.</summary>
    public DateTime? SweptAtUtc { get; set; }
}
