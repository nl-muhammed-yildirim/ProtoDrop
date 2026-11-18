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
    /// <remarks>
    /// Untracked: the context is a singleton, so a tracked read would be served from the identity map and hide
    /// out-of-band changes (e.g. AppUser.PlanId written by a plan change) until restart. Same idiom as UserPlanStore.
    /// </remarks>
    public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
        _db.Set<AppUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Email == lowercasedEmail, ct);

    public async Task CreateAsync(AppUser user, CancellationToken ct)
    {
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync(ct);
        _db.Entry(user).State = EntityState.Detached; // do not keep the row in the singleton identity map
    }

    /// <summary>T-008-06: session-user read — soft-deleted rows are treated as unknown (endpoint 13 → 404 NOT_FOUND). Untracked, see FindByEmailAsync.</summary>
    public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
        _db.Set<AppUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAtUtc == null, ct);

    /// <summary>T-008-06 (FR-008-4): partial profile update — null arguments are left alone; empty displayName falls back to the email local part.</summary>
    public async Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct)
    {
        var user = await _db.Set<AppUser>().FirstAsync(u => u.Id == userId, ct); // caller pre-checked via GetByIdAsync (no-row → 404 before this runs)
        if (displayName is not null)
        {
            var trimmed = displayName.Trim();
            user.DisplayName = string.IsNullOrEmpty(trimmed)
                ? user.Email.Split('@')[0]   // US-008-04 alternative flow: fallback to the email local part — never a blank "From:"
                : trimmed;
        }
        if (theme is not null)
            user.Theme = theme;              // caller validated against system/light/dark before this runs
        await _db.SaveChangesAsync(ct);
        _db.Entry(user).State = EntityState.Detached; // same reason as CreateAsync
    }

    /// <summary>T-008-06 / FR-008-8: soft-delete + transfer re-home in ONE transaction (mirrors ExpiryDeletionStore's idiom); US-047-02 adds the pending Erasure marker to the same transaction.</summary>
    public async Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
    {
        // Mirror ExpiryDeletionStore.DeleteTransfersAsync's idiom EXACTLY: await using tx, CommitAsync at the end — no try/catch.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Soft-delete the user row (30-day operator undo window — hard delete is P1 admin; US-008-06 edge case).
        var updatedUsers = await _db.Set<AppUser>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAtUtc, deletedAtUtc), ct);

        // 2. Re-home owned transfers: Status IN (0,1,3) — drafts/active/download-limit go anonymous; expired (2) rows
        //    die via the f-delete-transfers job anyway (Status values verified verbatim in Transfer.cs). SenderName is
        //    a snapshot column — never touched here (FR-002-4).
        var reHomed = await _db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == userId && t.Status != 2)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.OwnerAppUserId, (Guid?)null), ct);

        // 3. US-047-02 (FR-047-5): erasure marker, keyed on the userId — never the email (EC-047-5). Raw SQL bypasses the
        //    singleton change tracker (ExpiryStore idiom); the guard keeps the first deletion's row in a concurrent-DELETE race (EC-047-3).
        if (updatedUsers > 0)
        {
            await _db.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM [Erasure] WHERE [AppUserId] = {0}) " +
                "INSERT INTO [Erasure] ([AppUserId], [DeletedAtUtc], [ReHomedTransferCount]) VALUES ({0}, {1}, {2})",
                [userId, deletedAtUtc, reHomed], ct);
        }

        await tx.CommitAsync(ct);
        return updatedUsers > 0; // false only in the race where a concurrent DELETE already committed — handler maps to the same not-found shape
    }
}
