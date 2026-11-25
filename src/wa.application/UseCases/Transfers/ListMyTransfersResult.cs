using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>Page result: the rows plus the opaque next cursor (null = last page).</summary>
/// <param name="Items">Transfer rows of this page, newest first.</param>
/// <param name="NextCursor">Opaque cursor for the next page; null on the last page.</param>
public sealed record ListMyTransfersResult(IReadOnlyList<MyTransferRow> Items, string? NextCursor);
