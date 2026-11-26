using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-006-01: thin adapter over WaDbContext implementing IEmailStore.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class EmailStore(IDbContextFactory<WaDbContext> factory) : IEmailStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var transfer = await db.Set<Transfer>().FirstOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null)
            return null;

        // Query FileItems separately (no navigation property on Transfer).
        var files = await db.Set<FileItem>()
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
            files,
            transfer.OwnerAppUserId,
            transfer.Status,
            transfer.ScheduledSendAtUtc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var recipients = await db.Set<EmailRecipient>()
            .Where(r => r.TransferId == transferId)
            .ToListAsync(ct);
        return recipients;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(
        IReadOnlyCollection<string> addresses,
        string? senderEmail,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (addresses.Count == 0)
            return new HashSet<string>();

        var suppressed = await db.Set<EmailSuppression>()
            .Where(s => addresses.Contains(s.Address) &&
                (s.SenderEmail == senderEmail || s.SenderEmail == null))
            .Select(s => s.Address)
            .ToListAsync(ct);

        return new HashSet<string>(suppressed, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task MarkNotifiedAsync(
        IReadOnlyCollection<Guid> recipientIds,
        DateTime notifiedAtUtc,
        CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (recipientIds.Count == 0)
            return;

        // ExecuteUpdate with WHERE NotifiedAtUtc IS NULL (idempotent — replay won't double-set).
        await db.Set<EmailRecipient>()
            .Where(r => recipientIds.Contains(r.Id) && r.NotifiedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.NotifiedAtUtc, notifiedAtUtc), ct);
    }
}
