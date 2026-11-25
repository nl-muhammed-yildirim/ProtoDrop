namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft/{draftId}/finalize result (TA-4.2#2 response shape).</summary>
/// <param name="TransferId">Id of the created draft transfer.</param>
/// <param name="LinkId">Public 8-character link id.</param>
/// <param name="PublicUrl">Full public download URL built from the link id.</param>
/// <param name="Status">Stored status number of the transfer (a draft until it is sent).</param>
/// <param name="ExpiresAtUtc">UTC instant the transfer expires (set again at send time).</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="Files">The files that were finalized.</param>
public sealed record FinalizeTransferResult(
    Guid TransferId,
    string LinkId,
    string PublicUrl,
    int Status,
    DateTime ExpiresAtUtc,
    long TotalBytes,
    IReadOnlyList<FinalizedFile> Files);
