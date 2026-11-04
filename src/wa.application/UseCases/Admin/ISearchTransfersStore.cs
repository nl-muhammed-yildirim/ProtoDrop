namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): platform-wide admin transfer search — one bounded query over WaDbContext.
/// All filter args are passed IN (not parsed in the store) so handler composition is unit-testable from
/// wa.application.unit without EF — mirrors IOverviewStore's windows-in style.
/// </summary>
public interface ISearchTransfersStore
{
    /// <param name="q">Trimmed search term (null = no q predicate). Matches LinkId exactly OR any EmailRecipient.Address substring.</param>
    /// <param name="statuses">Status byte set (null = all statuses including deleted).</param>
    /// <param name="minBytes">Inclusive lower bound on TotalBytes (null = no bound).</param>
    /// <param name="maxBytes">Inclusive upper bound on TotalBytes (null = no bound).</param>
    /// <param name="fromUtc">Inclusive lower bound on CreatedAtUtc (null = no bound).</param>
    /// <param name="toUtc">Inclusive upper bound on CreatedAtUtc (null = no bound).</param>
    /// <param name="afterUtc">Cursor: rows with CreatedAtUtc &lt; afterUtc (null = first page).</param>
    /// <param name="afterIdExcluded">Cursor: rows with CreatedAtUtc == afterUtc AND Id &lt; afterIdExcluded (null = no tiebreak).</param>
    /// <param name="limit">Page size (TA-4.1.4: 25 max).</param>
    Task<(List<AdminTransferRow> Items, string? NextCursor)> SearchAsync(
        string? q,
        IReadOnlyCollection<byte>? statuses,
        long? minBytes,
        long? maxBytes,
        DateTime? fromUtc,
        DateTime? toUtc,
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}

/// <summary>T-011-04: one row in the admin transfer search results (TA-4.2#21).</summary>
public record AdminTransferRow(
    Guid Id,
    string LinkId,
    string? OwnerEmail,
    byte Status,
    int FileCount,
    long TotalBytes,
    DateTime CreatedAtUtc,
    int DownloadsCount);
