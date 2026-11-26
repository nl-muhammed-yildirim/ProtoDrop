using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-021-04: org logo path write surface. ExecuteUpdateAsync writes the column without loading or tracking the
/// row (same idiom as AdminFlagStore). Registered as a singleton; opens a fresh WaDbContext per call.
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class OrgLogoStore(IDbContextFactory<WaDbContext> factory) : IOrgLogoStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<bool> SetPathAsync(Guid userId, string? blobPath, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgLogoBlobPath, blobPath), ct);
        return rows > 0;
    }
}
