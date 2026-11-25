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
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="IdempotencyStore"/> class.</summary>
    /// <param name="db">Database context that backs the store.</param>
    /// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
    public IdempotencyStore(WaDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct)
    {
        var entry = await _db.Set<IdempotencyKey>().AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, ct);
        if (entry is null)
            return null;
        return new IdempotencyEntry(entry.Key, entry.ResultJson!, entry.StatusCode, entry.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct)
    {
        // Singleton context (adapter-owned): the finalize handler may already have tracked an
        // IdempotencyKey row for this key in its own transaction. Reuse that instance instead of
        // Add — a second tracked instance with the same PK ('Key') throws an EF identity conflict.
        var existing = _db.Set<IdempotencyKey>().Local.FirstOrDefault(k => k.Key == key)
            ?? await _db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == key, ct);

        if (existing is not null)
        {
            // Overwrite with the middleware-captured outcome (actual status + body).
            existing.ResultJson = resultJson;
            existing.StatusCode = statusCode;
            existing.ExpiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.AddHours(24); // TA-4.1.5: 24 h TTL from first use
        }
        else
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            _db.Set<IdempotencyKey>().Add(new IdempotencyKey
            {
                Key = key,
                ResultJson = resultJson,
                StatusCode = statusCode,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24) // TA-4.1.5: 24 h TTL contract
            });
        }

        await _db.SaveChangesAsync(ct);
    }
}
