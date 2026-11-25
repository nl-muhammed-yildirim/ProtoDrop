using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>Page result: the rows plus the opaque next cursor (null = last page).</summary>
public sealed record ListMyTransfersResult(IReadOnlyList<MyTransferRow> Items, string? NextCursor);
