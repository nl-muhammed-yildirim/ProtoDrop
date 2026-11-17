using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-006-01: thin adapter over WaDbContext implementing IEmailStore.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class EmailStore : IEmailStore
{
    private readonly WaDbContext _db;

    public EmailStore(WaDbContext db) => _db = db;

    public async Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct)
    {
        var transfer = await _db.Set<Transfer>().FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null)
            return null;

        // Query FileItems separately (no navigation property on Transfer).
        var files = await _db.Set<FileItem>()
            .Where(f => f.TransferId == transferId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FileEntry(f.OriginalName, f.SizeBytes))
            .ToListAsync(ct);

        return new EmailTransferContext(
            transfer.Id,
            transfer.SenderName,
            transfer.SenderEmail,
            transfer.ExpiresAtUtc,
            transfer.LinkId,
            files);
    }

    public async Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct)
    {
        var recipients = await _db.Set<EmailRecipient>()
            .Where(r => r.TransferId == transferId)
            .ToListAsync(ct);
        return recipients;
    }

    public async Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(
        IReadOnlyCollection<string> addresses,
        string? senderEmail,
        CancellationToken ct)
    {
        if (addresses.Count == 0)
            return new HashSet<string>();

        var suppressed = await _db.Set<EmailSuppression>()
            .Where(s => addresses.Contains(s.Address) &&
                (s.SenderEmail == senderEmail || s.SenderEmail == null))
            .Select(s => s.Address)
            .ToListAsync(ct);

        return new HashSet<string>(suppressed, StringComparer.OrdinalIgnoreCase);
    }

    public async Task MarkNotifiedAsync(
        IReadOnlyCollection<Guid> recipientIds,
        DateTime notifiedAtUtc,
        CancellationToken ct)
    {
        if (recipientIds.Count == 0)
            return;

        // ExecuteUpdate with WHERE NotifiedAtUtc IS NULL (idempotent — replay won't double-set).
        await _db.Set<EmailRecipient>()
            .Where(r => recipientIds.Contains(r.Id) && r.NotifiedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.NotifiedAtUtc, notifiedAtUtc), ct);
    }
}
