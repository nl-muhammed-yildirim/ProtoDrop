using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>One row of the My Files history list (US-009-01 happy-path payload — no extra round-trips per row).</summary>
/// <param name="Id">Transfer id.</param>
/// <param name="Name">Display name of the transfer, derived from its files.</param>
/// <param name="FileCount">Number of files in the transfer.</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="Status">Stored transfer status byte (see <c>TransferStatus</c>).</param>
/// <param name="ExpiresAtUtc">UTC instant the transfer expires.</param>
/// <param name="DownloadsCount">Downloads served so far.</param>
/// <param name="MaxDownloads">Download cap; -1 means unlimited.</param>
/// <param name="RecipientCount">Number of email recipients.</param>
/// <param name="LinkId">Public 8-character link id; the client builds the copy and view links from it.</param>
public sealed record MyTransferRow(
    Guid Id,
    string Name,
    int FileCount,
    long TotalBytes,
    byte Status,
    DateTime ExpiresAtUtc,
    int DownloadsCount,
    int MaxDownloads,
    int RecipientCount,
    // T-009-06 (FR-009-3 row actions): the 8-char Crockford linkId — Copy link / View build {origin}/t/{linkId} client-side (no extra round-trip).
    string LinkId);
