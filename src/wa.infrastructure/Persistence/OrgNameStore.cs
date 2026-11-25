using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>F-PRF-001: org name write surface (ExecuteUpdateAsync — same idiom as <see cref="OrgLogoStore"/>). Singleton in Program.cs.</summary>
/// <param name="db">Database context that backs the store.</param>
public sealed class OrgNameStore(WaDbContext db) : IOrgNameStore
{
    private readonly WaDbContext _db = db;

    /// <inheritdoc />
    public async Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct)
    {
        await _db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgName, orgName), ct);
    }
}
