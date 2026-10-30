using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-001-10: minimal DB surface the finalize handler needs (avoids a circular project reference to wa.infrastructure).
/// Implemented by <c>wa.infrastructure.Persistence.WaDbContext</c> via a thin adapter registered in Program.cs.
/// </summary>
public interface ITransferStore
{
    Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct);
    Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct);
    Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct);

    /// <summary>SUM(TotalBytes) WHERE OwnerAppUserId=@ownerId AND Status IN (1,3). Returns 0 when no rows.</summary>
    Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct);

    /// <summary>COUNT(*) WHERE OwnerAppUserId=@ownerId AND Status IN (1,3).</summary>
    Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct);

    /// <summary>
    /// T-009-01 (TA-4.1.4): owner-scoped My Files page — non-deleted transfers where OwnerAppUserId=@ownerId, ordered by
    /// CreatedAtUtc DESC, resumed after the given cursor row ((CreatedAtUtc &lt; @afterUtc) OR (= and Id &lt; @afterIdExcluded)),
    /// taking @limit+1 to detect a further page; NextCursor is encoded from the last returned row (null when no next page).
    /// T-009-02 (US-009-02): optional statuses byte set — rows where Status IN the set (null = no status predicate / all).
    /// </summary>
    Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct);

    /// <summary>
    /// T-009-03 (TA-4.2#15): owner-scoped My Files detail — non-deleted row where Id=@id AND OwnerAppUserId=@ownerId
    /// (both predicates in ONE query → foreign owner / guest / deleted are indistinguishable nulls), plus the files +
    /// recipients projections; null when no match. Three bounded queries total (AC-009-1: no extra round-trips per row).
    /// </summary>
    Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct);
}
