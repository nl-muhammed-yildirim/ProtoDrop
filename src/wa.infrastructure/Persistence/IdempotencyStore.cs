using Microsoft.EntityFrameworkCore;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-002-02 (TA-4.1.5): thin adapter over WaDbContext implementing IIdempotencyStore —
/// the generic HTTP-level idempotency storage used by IdempotencyMiddleware.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed class IdempotencyStore(IDbContextFactory<WaDbContext> factory, TimeProvider timeProvider) : IIdempotencyStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entry = await db.Set<IdempotencyKey>().AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, ct);
        if (entry is null)
            return null;
        return new IdempotencyEntry(entry.Key, entry.ResultJson!, entry.StatusCode, entry.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // An expired key row may already exist (pre-seeded by T-002-04, or written by the finalize transaction):
        // update it instead of a blind INSERT (PK_Idem).
        var existing = await db.Set<IdempotencyKey>().FirstOrDefaultAsync(k => k.Key == key, ct);

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
            db.Set<IdempotencyKey>().Add(new IdempotencyKey
            {
                Key = key,
                ResultJson = resultJson,
                StatusCode = statusCode,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(24) // TA-4.1.5: 24 h TTL contract
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
