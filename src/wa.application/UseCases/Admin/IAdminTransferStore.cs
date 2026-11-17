using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-05 (TA-4.2#21 / FR-011-4): admin force-delete — platform-scoped predicate Id=@id AND Status != 4
/// (NO owner predicate — the admin can target guest transfers where OwnerAppUserId is NULL).
/// null on no match = already-deleted / unknown (endpoint → NOT_FOUND; EC-009-2 idempotency reused).
/// Reuses MarkDeleteResult from wa.application.UseCases.Transfers.
/// </summary>
public interface IAdminTransferStore
{
    /// <summary>
    /// T-011-05: admin force-delete — soft-delete + RefCount decrement (mirrors TransferStore.MarkDeletedAsync
    /// at src/wa.infrastructure/Persistence/TransferStore.cs:143 with the owner predicate removed).
    /// Returns null when the row is already-deleted or unknown.
    /// </summary>
    Task<MarkDeleteResult?> AdminMarkDeletedAsync(Guid id, DateTime deletedAtUtc, CancellationToken ct);
}
