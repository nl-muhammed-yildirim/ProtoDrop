using wa.application.Limits;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-04: flat result — items + nextCursor (null when no next page).</summary>
public record SearchTransfersResult(
    IReadOnlyList<AdminTransferRow> Items,
    string? NextCursor);
