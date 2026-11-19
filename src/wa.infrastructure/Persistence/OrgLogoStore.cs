using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// US-021-04: org logo path write surface. ExecuteUpdateAsync keeps the singleton, shared WaDbContext free of tracked
/// entities (same idiom as AdminFlagStore). Registered as a singleton in Program.cs.
/// </summary>
public sealed class OrgLogoStore : IOrgLogoStore
{
    private readonly WaDbContext _db;

    public OrgLogoStore(WaDbContext db) => _db = db;

    public async Task SetPathAsync(Guid userId, string? blobPath, CancellationToken ct)
    {
        await _db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrgLogoBlobPath, blobPath), ct);
    }
}
