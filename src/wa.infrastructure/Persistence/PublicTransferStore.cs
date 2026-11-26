using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-014: thin adapter over WaDbContext implementing IPublicTransferStore — the only DB surface the recipient-page handler needs.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class PublicTransferStore(IDbContextFactory<WaDbContext> factory, TimeProvider timeProvider) : IPublicTransferStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    /// <remarks>
    /// AsNoTracking: this is a read-only query — the cap flip in RecordSingleFileDownloadAsync writes via raw SQL (the guard lives in
    /// the UPDATE itself) and every call opens its own context, so no tracked copy can return stale values for that row
    /// (FR-003-9 byte identity, CapReached_410_BodyByteIdenticalToEndpoint4). No handler mutates or saves this entity, so an
    /// untracked read is safe for all three callers (GetPublicTransfer/GetFileDownloadUrl/UnlockTransfer — status checks + email only).
    /// </remarks>
    public async Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var transfer = await db.Set<Transfer>().AsNoTracking().FirstOrDefaultAsync(t => t.LinkId == linkId, ct);
        if (transfer is null) return (null, []);

        // FileItem has no navigation from Transfer — separate query, projected to plain rows (no EF entities in use cases; golden rule 8).
        var files = await db.Set<FileItem>()
            .Where(f => f.TransferId == transfer.Id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new PublicFileItem(f.Id, f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);
        return (transfer, files);
    }

    /// <inheritdoc />
    /// <remarks>
    /// T-003-06 (TA-7.2 step 4): atomically record a single-file download against the transfer's cap.
    /// The increment is a guarded ExecuteUpdate (no read-then-write race — a burst of parallel mints can never count past MaxDownloads);
    /// when the guard rejects, the status flips to 3 via US-005-03's literal idempotent UPDATE (the flip happens here, not in a worker).
    /// DownloadEvent insertion follows a successful increment only — a rejected mint leaves no trace.
    /// </remarks>
    public async Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(
        Guid transferId,
        Guid fileId,
        string? ipHash,
        string? uaHash,
        string? userAgentFamily,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TA-7.2 step 4: the guard runs in the UPDATE itself — rowsAffected==1 iff the download is within cap at execution time.
        var rowsAffected = await db.Set<Transfer>()
            .Where(t => t.Id == transferId && (t.MaxDownloads < 0 || t.DownloadsCount < t.MaxDownloads))
            .ExecuteUpdateAsync(setter => setter.SetProperty(t => t.DownloadsCount, t => t.DownloadsCount + 1), ct);

        if (rowsAffected is not 1)
        {
            // US-005-03 technical note literal: idempotent flip — Status=3 + ExpiredAtUtc set when it flips (grace clock starts),
            // WHERE Id=@id AND Status IN (1,5) so concurrent racers all write the same value.
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE [Transfer] SET [Status]=3, [ExpiredAtUtc]=COALESCE([ExpiredAtUtc], GETUTCDATE()) WHERE [Id] = {0} AND [Status] IN (1, 5)",
                [transferId], ct);

            // The raw-SQL UPDATE bypasses the change tracker: detach any tracked copy so later SaveChanges can't resurrect the old
            // Status/DownloadsCount (GetByLinkIdAsync reads untracked). T-003-10: a real detach — Local.Remove marks the row Deleted.
            db.DetachTransfer(transferId);

            return new SingleFileDownloadResult(SingleFileDownloadOutcome.CapReached);
        }

        // Within cap: the DownloadEvent row (TA-3.2; IpHash/UaHash already PII-transformed upstream — TA-9.4/10.5).
        db.Set<DownloadEvent>().Add(new DownloadEvent
        {
            TransferId = transferId,
            FileId = fileId,
            IpHash = ipHash,
            UaHash = uaHash,
            UserAgentFamily = userAgentFamily,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
        return new SingleFileDownloadResult(SingleFileDownloadOutcome.Recorded);
    }

    /// <inheritdoc />
    /// <remarks>
    /// T-004-02 (TA-7.2): atomically record a zip download against the transfer's cap.
    /// Same as RecordSingleFileDownloadAsync but with fileId = null (download-all).
    /// The increment is a guarded ExecuteUpdate (no read-then-write race — a burst of parallel mints can never count past MaxDownloads);
    /// when the guard rejects, the status flips to 3 via US-005-03's literal idempotent UPDATE (the flip happens here, not in a worker).
    /// DownloadEvent insertion follows a successful increment only — a rejected mint leaves no trace.
    /// </remarks>
    public async Task<SingleFileDownloadResult> RecordZipDownloadAsync(
        Guid transferId,
        string? ipHash,
        string? uaHash,
        string? userAgentFamily,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // TA-7.2 step 4: the guard runs in the UPDATE itself — rowsAffected==1 iff the download is within cap at execution time.
        var rowsAffected = await db.Set<Transfer>()
            .Where(t => t.Id == transferId && (t.MaxDownloads < 0 || t.DownloadsCount < t.MaxDownloads))
            .ExecuteUpdateAsync(setter => setter.SetProperty(t => t.DownloadsCount, t => t.DownloadsCount + 1), ct);

        if (rowsAffected is not 1)
        {
            // US-005-03 technical note literal: idempotent flip — Status=3 + ExpiredAtUtc set when it flips (grace clock starts),
            // WHERE Id=@id AND Status IN (1,5) so concurrent racers all write the same value.
            _ = await db.Database.ExecuteSqlRawAsync(
                "UPDATE [Transfer] SET [Status]=3, [ExpiredAtUtc]=COALESCE([ExpiredAtUtc], GETUTCDATE()) WHERE [Id] = {0} AND [Status] IN (1, 5)",
                [transferId], ct);

            // The raw-SQL UPDATE bypasses the change tracker: detach any tracked copy so later SaveChanges can't resurrect the old
            // Status/DownloadsCount (GetByLinkIdAsync reads untracked). T-003-10: a real detach — Local.Remove marks the row Deleted.
            db.DetachTransfer(transferId);

            return new SingleFileDownloadResult(SingleFileDownloadOutcome.CapReached);
        }

        // Within cap: the DownloadEvent row (TA-3.2; IpHash/UaHash already PII-transformed upstream — TA-9.4/10.5).
        // FileId = null for download-all (TA-3.2: NULL = download-all).
        db.Set<DownloadEvent>().Add(new DownloadEvent
        {
            TransferId = transferId,
            FileId = null, // download-all
            IpHash = ipHash,
            UaHash = uaHash,
            UserAgentFamily = userAgentFamily,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
        return new SingleFileDownloadResult(SingleFileDownloadOutcome.Recorded);
    }
}
