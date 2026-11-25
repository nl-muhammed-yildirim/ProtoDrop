using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): read port for AuditLog — last count rows, newest first.
/// DECISION: separate from IAuditStore (the write port pinned as "one bounded insert per call").
/// </summary>
public interface IAuditReadStore
{
    /// <summary>Last count rows, newest first (no CreatedAtUtc tiebreak — Id is BIGINT IDENTITY).</summary>
    /// <param name="count">Maximum number of rows to return.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>Up to <paramref name="count"/> audit rows, newest first.</returns>
    Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count, CancellationToken ct);
}
