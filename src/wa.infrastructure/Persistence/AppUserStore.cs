using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-008-01: thin adapter over WaDbContext implementing IAppUserStore — the only DB surface signup needs.
/// Registered as a singleton in Program.cs (same lifetime as the adapter-owned WaDbContext).
/// </summary>
public sealed class AppUserStore : IAppUserStore
{
    private readonly WaDbContext _db;

    public AppUserStore(WaDbContext db) => _db = db;

    /// <summary>
    /// EC-008-1: AppUser.Email is stored lowercased + trimmed and the unique index UQ_AppUser_Email keys on that form —
    /// callers already pass the normalized email, so this is an exact-column match.
    /// </summary>
    public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
        _db.Set<AppUser>().FirstOrDefaultAsync(u => u.Email == lowercasedEmail, ct);

    public async Task CreateAsync(AppUser user, CancellationToken ct)
    {
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync(ct);
    }
}
