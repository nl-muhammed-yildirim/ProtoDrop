using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-002-04: minimal DB surface the send handler needs.
/// Implemented by a thin adapter in wa.infrastructure.Persistence registered in Program.cs.
/// </summary>
public interface ISendStore
{
    Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct);
    Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct);

    // T-010-01 (AC-010-1): three new members for the re-send use case — no changes to existing signatures.
    /// <summary>T-010-01: read all recipient email addresses stored for a transfer.</summary>
    Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct);

    /// <summary>T-010-01 (AC-010-1): list file item rows (metadata only, no EF entities) ordered by SortOrder.</summary>
    Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct);

    /// <summary>
    /// T-010-01 (AC-010-1): one transaction — insert the new draft Transfer + new FileItem rows + the materialized
    /// snapshot EmailRecipient rows (the editable pending state US-010-02 reads), then increment RefCount on each shared
    /// BlobRef via guarded ExecuteUpdate (same raw-SQL guard style as PublicTransferStore.RecordSingleFileDownloadAsync;
    /// rowsAffected &lt; 1 → InvalidOperationException). T-010-02 will insert its FILES_GONE gate inside this same transaction.
    /// </summary>
    Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct);
}
