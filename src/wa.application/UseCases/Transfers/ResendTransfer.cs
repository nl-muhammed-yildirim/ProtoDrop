using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/resend request (endpoint 16, T-010-01). Cookie required — the session owner is passed in.</summary>
public sealed record ResendTransferCommand(Guid TransferId, Guid? OwnerAppUserId = null) : IRequest<ResendTransferResult?>;

/// <summary>One file row in the re-send result (endpoint 16's JSON + T-010-03's banner copy).</summary>
public sealed record ResendFileItem(Guid FileId, string Name);

/// <summary>POST /api/v1/transfers/{id}/resend result — the new draft with pre-filled fields for T-010-03's UI.</summary>
public sealed record ResendTransferResult(
    Guid TransferId,
    string LinkId,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<string> Recipients,
    string? Note,
    string SenderName,
    string? SenderEmail,
    bool PasswordRequired,
    IReadOnlyList<ResendFileItem> Files);

/// <summary>
/// T-010-01 (FR-010-1/2/3): create a new DRAFT transfer that shares the original's blobs via BlobRef/FileItem junction.
/// Guest owner → ResendForbiddenException; foreign owner / unknown / Status=4 → null (endpoint maps to 404 NOT_FOUND).
/// </summary>
public sealed class ResendTransferHandler : IRequestHandler<ResendTransferCommand, ResendTransferResult?>
{
    private readonly ISendStore _store;
    private readonly ITransferStore _transferStore;
    private readonly ILimitsProvider _limits;

    public ResendTransferHandler(
        ISendStore store,
        ITransferStore transferStore,
        ILimitsProvider limits)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _transferStore = transferStore ?? throw new ArgumentNullException(nameof(transferStore));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public async Task<ResendTransferResult?> Handle(ResendTransferCommand request, CancellationToken cancellationToken)
    {
        // 1. Load original — null result is the endpoint's 404 path.
        var original = await _store.FindByIdAsync(request.TransferId, cancellationToken);
        if (original is null)
            return null;

        // 2. Owner scope (task file's pinned constraint): guest row → FORBIDDEN (EC-010-4); foreign owner → NOT_FOUND (no enumeration).
        if (original.OwnerAppUserId is null)
            throw new ResendForbiddenException();

        if (original.OwnerAppUserId != request.OwnerAppUserId)
            return null;

        // 3. Status == 4 (Deleted) → 404 NOT_FOUND path — no re-send of a deleted row.
        if (original.Status == 4)
            return null;

        // 4. Mint LinkId with collision retry (same once-and-retry pattern as FinalizeTransferHandler lines 84–90).
        var linkId = wa.domain.ValueObjects.LinkId.Mint();
        if (await _transferStore.TransferExistsByLinkIdAsync(linkId.Value, cancellationToken))
        {
            linkId = wa.domain.ValueObjects.LinkId.Mint();
            if (await _transferStore.TransferExistsByLinkIdAsync(linkId.Value, cancellationToken))
                throw new InvalidOperationException($"LinkId collision after retry: {linkId.Value}");
        }

        // Resolve plan limits — never a literal (TA-3.4).
        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, cancellationToken);

        var now = DateTime.UtcNow;
        var transferId = Guid.NewGuid();

        // 5. Build new Transfer (Status=0 Draft). Copies verbatim from the original (FR-010-2): TotalBytes/FileCount,
        //    OwnerAppUserId kept, SenderName/SenderEmail/Note/PasswordHash copied byte-for-byte. PasswordHash is PBKDF2 keyed by
        //    transfer id but the column value IS the snapshot — don't re-key here (FR-010-2). SourcedFrom = original.Id so
        //    SaveSendAsync flips SupersededBy on the original at send time.
        var draft = new Transfer
        {
            Id = transferId,
            LinkId = linkId.Value,
            Status = 0,
            ExpiresAtUtc = now.AddDays(limits.RetentionDays),
            DownloadsCount = 0,
            MaxDownloads = 1,
            TotalBytes = original.TotalBytes,
            FileCount = original.FileCount,
            OwnerAppUserId = original.OwnerAppUserId,
            SenderName = original.SenderName,
            SenderEmail = original.SenderEmail,
            Note = original.Note,
            PasswordHash = original.PasswordHash,
            SourcedFrom = original.Id,
            CreatedAtUtc = now
        };

        // 6. New FileItem rows with FRESH Guids, same OriginalName/SizeBytes/ContentType/SortOrder and SAME BlobRefId.
        var fileItems = (await _store.ListFileItemsByTransferAsync(original.Id, cancellationToken))
            .Select(row => new FileItem
            {
                Id = Guid.NewGuid(),
                TransferId = transferId,
                BlobRefId = row.BlobRefId,
                OriginalName = row.OriginalName,
                SizeBytes = row.SizeBytes,
                ContentType = row.ContentType,
                SortOrder = row.SortOrder
            })
            .ToList();

        // 7. New EmailRecipient rows carrying the original's addresses (the editable pending state US-010-02 reads).
        var recipientAddresses = await _store.GetRecipientAddressesAsync(original.Id, cancellationToken);
        var recipients = recipientAddresses.Select(a => new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            Address = a
        }).ToList();

        // 8. RefCount++ on each shared BlobRef — guarded ExecuteUpdate in SaveResendAsync (steps 5–8 in ONE transaction).
        var sharedBlobRefs = fileItems.Select(f => f.BlobRefId)
            .Distinct()
            .Select(id => new BlobRef { Id = id })
            .ToList();

        await _store.SaveResendAsync(draft, fileItems, recipients, sharedBlobRefs, cancellationToken);

        return new ResendTransferResult(
            transferId,
            linkId.Value,
            0, // Draft
            draft.ExpiresAtUtc,
            draft.TotalBytes,
            draft.FileCount,
            recipientAddresses,
            draft.Note,
            draft.SenderName,
            draft.SenderEmail,
            !string.IsNullOrEmpty(draft.PasswordHash),
            fileItems.Select(f => new ResendFileItem(f.Id, f.OriginalName)).ToList());
    }
}

/// <summary>
/// A guest-row re-send attempt (EC-010-4). The endpoint catches this and throws
/// BusinessException(ProblemCodes.Forbidden) — keeps the closed code list intact.
/// </summary>
public sealed class ResendForbiddenException : Exception
{
    public ResendForbiddenException() : base("This transfer has no owner to re-send from.") { }
}
