using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-002-02 (TA-4.1.5): thin adapter over WaDbContext implementing IIdempotencyStore —
/// the generic HTTP-level idempotency storage used by IdempotencyMiddleware.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly WaDbContext _db;

    public IdempotencyStore(WaDbContext db) => _db = db;

    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct)
    {
        var entry = await _db.Set<IdempotencyKey>().AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, ct);
        if (entry is null)
            return null;
        return new IdempotencyEntry(entry.Key, entry.ResultJson!, entry.StatusCode, entry.ExpiresAtUtc);
    }

    public async Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        _db.Set<IdempotencyKey>().Add(new IdempotencyKey
        {
            Key = key,
            ResultJson = resultJson,
            StatusCode = statusCode,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24) // TA-4.1.5: 24 h TTL contract
        });
        await _db.SaveChangesAsync(ct);
    }
}
