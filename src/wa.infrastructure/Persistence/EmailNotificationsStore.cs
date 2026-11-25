using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.EmailSettings;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>F-XCT-003: AppUser.EmailNotificationsEnabled read/write — fresh context per call (safe under f-email concurrency). Singleton in both hosts.</summary>
public sealed class EmailNotificationsStore : IEmailNotificationsStore
{
    private readonly IDbContextFactory<WaDbContext> _factory;

    /// <summary>Initializes a new instance of the <see cref="EmailNotificationsStore"/> class.</summary>
    /// <param name="factory">The factory.</param>
    public EmailNotificationsStore(IDbContextFactory<WaDbContext> factory) => _factory = factory;

    /// <inheritdoc />
    public async Task SetAsync(Guid userId, bool enabled, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailNotificationsEnabled, (bool?)enabled), ct);
    }

    /// <inheritdoc />
    public async Task<bool?> GetAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .Select(u => u.EmailNotificationsEnabled)
            .FirstOrDefaultAsync(ct);
    }
}
