using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>F-PRF-001: org name write surface (ExecuteUpdateAsync — same idiom as <see cref="OrgLogoStore"/>). Singleton; fresh WaDbContext per call.</summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class OrgNameStore(IDbContextFactory<WaDbContext> factory) : IOrgNameStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgName, orgName), ct);
    }
}
