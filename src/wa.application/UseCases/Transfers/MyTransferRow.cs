using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>One row of the My Files history list (US-009-01 happy-path payload — no extra round-trips per row).</summary>
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
