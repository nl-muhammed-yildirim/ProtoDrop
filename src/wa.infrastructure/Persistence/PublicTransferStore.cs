using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-014: thin adapter over WaDbContext implementing IPublicTransferStore — the only DB surface the recipient-page handler needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class PublicTransferStore : IPublicTransferStore
{
    private readonly WaDbContext _db;

    public PublicTransferStore(WaDbContext db) => _db = db;

    /// <summary>
    /// AsNoTracking is load-bearing, not style: RecordSingleFileDownloadAsync's cap flip writes via raw SQL (the guard lives in
    /// the UPDATE itself), which bypasses the change tracker. On the singleton context a tracked read then returns the seed-era
    /// values for that row even after the entity was detached — endpoint 4 would answer Active instead of DownloadLimit and lose
    /// FR-003-9's byte identity (CapReached_410_BodyByteIdenticalToEndpoint4). No handler mutates or saves this entity, so an
    /// untracked read is safe for all three callers (GetPublicTransfer/GetFileDownloadUrl/UnlockTransfer — status checks + email only).
    /// </summary>
    public async Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct)
    {
        var transfer = await _db.Set<Transfer>().AsNoTracking().FirstOrDefaultAsync(t => t.LinkId == linkId, ct);
        if (transfer is null) return (null, []);

        // FileItem has no navigation from Transfer — separate query, projected to plain rows (no EF entities in use cases; golden rule 8).
        var files = await _db.Set<FileItem>()
            .Where(f => f.TransferId == transfer.Id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new PublicFileItem(f.Id, f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);
        return (transfer, files);
    }

    /// <summary>
    /// T-003-06 (TA-7.2 step 4): atomically record a single-file download against the transfer's cap.
    /// The increment is a guarded ExecuteUpdate (no read-then-write race — a burst of parallel mints can never count past MaxDownloads);
    /// when the guard rejects, the status flips to 3 via US-005-03's literal idempotent UPDATE (the flip happens here, not in a worker).
    /// DownloadEvent insertion follows a successful increment only — a rejected mint leaves no trace.
    /// </summary>
    public async Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(
        Guid transferId,
        Guid fileId,
        string? ipHash,
        string? uaHash,
        CancellationToken ct)
    {
        // TA-7.2 step 4: the guard runs in the UPDATE itself — rowsAffected==1 iff the download is within cap at execution time.
        var rowsAffected = await _db.Set<Transfer>()
            .Where(t => t.Id == transferId && (t.MaxDownloads < 0 || t.DownloadsCount < t.MaxDownloads))
            .ExecuteUpdateAsync(setter => setter.SetProperty(t => t.DownloadsCount, t => t.DownloadsCount + 1), ct);

        if (rowsAffected is not 1)
        {
            // US-005-03 technical note literal: idempotent flip — Status=3 + ExpiredAtUtc set when it flips (grace clock starts),
            // WHERE Id=@id AND Status=1 so concurrent racers all write the same value.
            _ = await _db.Database.ExecuteSqlRawAsync(
                "UPDATE [Transfer] SET [Status]=3, [ExpiredAtUtc]=COALESCE([ExpiredAtUtc], GETUTCDATE()) WHERE [Id] = {0} AND [Status] = 1",
                [transferId], ct);

            // The raw-SQL UPDATE bypasses the change tracker: if this transfer is tracked in the singleton context, detach it so
            // later SaveChanges can't resurrect the old Status/DownloadsCount (GetByLinkIdAsync reads untracked — see its summary).
            var tracked = _db.Set<Transfer>().Local.FirstOrDefault(t => t.Id == transferId);
            if (tracked is not null)
                _db.Set<Transfer>().Local.Remove(tracked);

            return new SingleFileDownloadResult(SingleFileDownloadOutcome.CapReached);
        }

        // Within cap: the DownloadEvent row (TA-3.2; IpHash/UaHash already PII-transformed upstream — TA-9.4/10.5).
        _db.Set<DownloadEvent>().Add(new DownloadEvent
        {
            TransferId = transferId,
            FileId = fileId,
            IpHash = ipHash,
            UaHash = uaHash,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return new SingleFileDownloadResult(SingleFileDownloadOutcome.Recorded);
    }
}
