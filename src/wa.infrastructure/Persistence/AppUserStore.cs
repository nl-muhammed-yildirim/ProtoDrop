using Microsoft.EntityFrameworkCore;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-008-01: thin adapter over WaDbContext implementing IAppUserStore — the only DB surface signup needs.
/// Registered as a singleton; opens a fresh WaDbContext per call (CHORE-dbcontext-lifetime).
/// </summary>
/// <param name="factory">Factory that creates a short-lived database context per call.</param>
public sealed class AppUserStore(IDbContextFactory<WaDbContext> factory) : IAppUserStore
{
    private readonly IDbContextFactory<WaDbContext> _factory = factory;

    /// <inheritdoc />
    public async Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Email == lowercasedEmail, ct);
    }

    /// <inheritdoc />
    public async Task CreateAsync(AppUser user, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<AppUser>().Add(user);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>T-008-06: session-user read — soft-deleted rows are treated as unknown (endpoint 13 → 404 NOT_FOUND). Untracked, see FindByEmailAsync.</remarks>
    public async Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAtUtc == null, ct);
    }

    /// <inheritdoc />
    /// <remarks>T-008-06 (FR-008-4): partial profile update — null arguments are left alone; empty displayName falls back to the email local part.</remarks>
    public async Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Set<AppUser>().FirstAsync(u => u.Id == userId, ct); // caller pre-checked via GetByIdAsync (no-row → 404 before this runs)
        if (displayName is not null)
        {
            var trimmed = displayName.Trim();
            user.DisplayName = string.IsNullOrEmpty(trimmed)
                ? user.Email.Split('@')[0]   // US-008-04 alternative flow: fallback to the email local part — never a blank "From:"
                : trimmed;
        }
        if (theme is not null)
            user.Theme = theme;              // caller validated against system/light/dark before this runs
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var updated = await db.Set<AppUser>()
            .Where(u => u.Id == userId && u.DeletedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.EmailConfirmed, true)
                .SetProperty(u => u.PasswordHash, (string?)null)
                .SetProperty(u => u.SessionsValidAfterUtc, (DateTime?)nowUtc), ct);
        return updated > 0;
    }

    /// <inheritdoc />
    public async Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<AppUser>().AsNoTracking().Where(u => u.Id == userId).Select(u => u.SessionsValidAfterUtc).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>T-008-06 / FR-008-8: soft-delete + transfer re-home in ONE transaction (mirrors ExpiryDeletionStore's idiom); US-047-02 adds the pending Erasure marker to the same transaction.</remarks>
    public async Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Mirror ExpiryDeletionStore.DeleteTransfersAsync's idiom EXACTLY: await using tx, CommitAsync at the end — no try/catch.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1. Soft-delete the user row (30-day operator undo window — hard delete is P1 admin; US-008-06 edge case).
        var updatedUsers = await db.Set<AppUser>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAtUtc, deletedAtUtc), ct);

        // 2. Re-home owned transfers: Status IN (0,1,3) — drafts/active/download-limit go anonymous; expired (2) rows
        //    die via the f-delete-transfers job anyway (Status values verified verbatim in Transfer.cs). SenderName is
        //    a snapshot column — never touched here (FR-002-4).
        var reHomed = await db.Set<Transfer>()
            .Where(t => t.OwnerAppUserId == userId && t.Status != 2)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.OwnerAppUserId, (Guid?)null), ct);

        // 3. US-047-02 (FR-047-5): erasure marker, keyed on the userId — never the email (EC-047-5). Raw SQL bypasses the
        //    change tracker (ExpiryStore idiom); the guard keeps the first deletion's row in a concurrent-DELETE race (EC-047-3).
        if (updatedUsers > 0)
        {
            await db.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM [Erasure] WHERE [AppUserId] = {0}) " +
                "INSERT INTO [Erasure] ([AppUserId], [DeletedAtUtc], [ReHomedTransferCount]) VALUES ({0}, {1}, {2})",
                [userId, deletedAtUtc, reHomed], ct);
        }

        await tx.CommitAsync(ct);
        return updatedUsers > 0; // false only in the race where a concurrent DELETE already committed — handler maps to the same not-found shape
    }
}
