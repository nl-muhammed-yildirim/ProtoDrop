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
    /// <summary>Loads a transfer by its primary key.</summary>
    /// <param name="id">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The transfer, or <see langword="null"/> when it does not exist.</returns>
    Task<Transfer?> FindByIdAsync(Guid id, CancellationToken ct);
    /// <summary>Persists the sent transfer (status, expiry, password, schedule) together with its recipient rows.</summary>
    /// <param name="transfer">The transfer in its final send state.</param>
    /// <param name="recipients">New recipient rows to insert.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveSendAsync(Transfer transfer, IReadOnlyList<EmailRecipient> recipients, CancellationToken ct);

    /// <summary>T-010-01: read all recipient email addresses stored for a transfer.</summary>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The recipient addresses stored for the transfer; PII (TA-9.4).</returns>
    /// <remarks>
    /// T-010-01 (AC-010-1): three new members for the re-send use case — no changes to existing signatures.
    /// </remarks>
    Task<IReadOnlyList<string>> GetRecipientAddressesAsync(Guid transferId, CancellationToken ct);

    /// <summary>T-010-01 (AC-010-1): list file item rows (metadata only, no EF entities) ordered by SortOrder.</summary>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The file rows of the transfer, ordered by sort order.</returns>
    Task<IReadOnlyList<FileItemRow>> ListFileItemsByTransferAsync(Guid transferId, CancellationToken ct);

    /// <summary>
    /// T-010-01 (AC-010-1): one transaction — insert the new draft Transfer + new FileItem rows + the materialized
    /// snapshot EmailRecipient rows (the editable pending state US-010-02 reads), then increment RefCount on each shared
    /// BlobRef via guarded ExecuteUpdate (same raw-SQL guard style as PublicTransferStore.RecordSingleFileDownloadAsync;
    /// rowsAffected &lt; 1 → InvalidOperationException). T-010-02 will insert its FILES_GONE gate inside this same transaction.
    /// </summary>
    /// <param name="draft">The new draft transfer created from the original.</param>
    /// <param name="fileItems">New file rows that point at the original blobs.</param>
    /// <param name="recipients">Snapshot of the original recipients as editable pending rows.</param>
    /// <param name="sharedBlobRefs">Blob references whose ref count is incremented for the new transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveResendAsync(Transfer draft, IReadOnlyList<FileItem> fileItems, IReadOnlyList<EmailRecipient> recipients, IReadOnlyList<BlobRef> sharedBlobRefs, CancellationToken ct);
}
