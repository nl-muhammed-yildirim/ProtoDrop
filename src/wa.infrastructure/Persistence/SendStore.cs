using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-002-04: thin adapter over WaDbContext implementing ISendStore — the only DB surface the send handler needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class SendStore : ISendStore
{
    private readonly WaDbContext _db;

    public SendStore(WaDbContext db) => _db = db;

    public Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct) =>
        _db.Set<Transfer>().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        _db.Set<Transfer>().Update(transfer);
        foreach (var r in recipients)
            _db.Set<EmailRecipient>().Add(r);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
