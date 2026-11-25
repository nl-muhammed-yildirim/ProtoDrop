using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>One row of the My Files history list (US-009-01 happy-path payload — no extra round-trips per row).</summary>
/// <param name="Id">Identifier of the my transfer row.</param>
/// <param name="Name">The name of the my transfer row.</param>
/// <param name="FileCount">The file count of the my transfer row.</param>
/// <param name="TotalBytes">The total bytes of the my transfer row.</param>
/// <param name="Status">The status of the my transfer row.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the my transfer row.</param>
/// <param name="DownloadsCount">The downloads count of the my transfer row.</param>
/// <param name="MaxDownloads">The max downloads of the my transfer row.</param>
/// <param name="RecipientCount">The recipient count of the my transfer row.</param>
/// <param name="LinkId">Identifier of the link of the my transfer row.</param>
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
