namespace wa.domain.Privacy;

/// <summary>
/// The single closed list of persisted raw-PII columns (F-XCT-005 FR-048-2, US-048-03). It must equal the set of <see cref="PiiAttribute"/>
/// properties; a convention test enforces both directions, so adding a PII column is a reviewed diff here.
/// </summary>
public static class ErasureScope
{
    private const string SoftDelete = "soft-deleted by DELETE /auth/me (AppUserStore.SoftDeleteWithReHomeAsync); no hard delete exists yet";
    private const string ExpiryDelete = "row removed by f-delete-transfers (ExpiryDeletionStore.DeleteTransfersAsync)";

    /// <summary>All 15 raw-PII columns with their erasure disposition as implemented today.</summary>
    public static IReadOnlyList<ErasureColumn> AllColumns { get; } =
    [
        new(typeof(AppUser), nameof(AppUser.Email), ErasureDisposition.AccountSoftDelete, SoftDelete),
        new(typeof(AppUser), nameof(AppUser.DisplayName), ErasureDisposition.AccountSoftDelete, SoftDelete),
        new(typeof(AppUser), nameof(AppUser.OrgName), ErasureDisposition.AccountSoftDelete, SoftDelete),
        new(typeof(EmailRecipient), nameof(EmailRecipient.Address), ErasureDisposition.TransferHardDelete, ExpiryDelete),
        new(typeof(FileItem), nameof(FileItem.OriginalName), ErasureDisposition.TransferHardDelete, ExpiryDelete),
        new(typeof(AuthToken), nameof(AuthToken.Email), ErasureDisposition.Retained, "magic-link rows are never purged; scrub is a follow-up"),
        new(typeof(AuditLog), nameof(AuditLog.ActorEmail), ErasureDisposition.Retained, "admin audit trail is kept"),
        new(typeof(AuditLog), nameof(AuditLog.DetailsJson), ErasureDisposition.Retained, "admin audit trail is kept"),
        new(typeof(DomainEvent), nameof(DomainEvent.EnvelopeJson), ErasureDisposition.Retained, "outbox rows are not pruned; scrub is a follow-up"),
        new(typeof(DomainEvent), nameof(DomainEvent.PartitionKey), ErasureDisposition.Retained, "outbox rows are not pruned; scrub is a follow-up"),
        new(typeof(EmailSuppression), nameof(EmailSuppression.Address), ErasureDisposition.Retained, "an opt-out must outlive the account"),
        new(typeof(EmailSuppression), nameof(EmailSuppression.SenderEmail), ErasureDisposition.Retained, "an opt-out must outlive the account"),
        new(typeof(Transfer), nameof(Transfer.SenderName), ErasureDisposition.Retained, "EC-048-1: anonymized attribution kept on re-home; the Transfer row is only soft-deleted"),
        new(typeof(Transfer), nameof(Transfer.SenderEmail), ErasureDisposition.Retained, "kept on re-home today; scrub is a follow-up"),
        new(typeof(IdempotencyKey), nameof(IdempotencyKey.ResultJson), ErasureDisposition.Retained, "idempotency replay body carries file names; rows are not purged; scrub is a follow-up"),
    ];
}
