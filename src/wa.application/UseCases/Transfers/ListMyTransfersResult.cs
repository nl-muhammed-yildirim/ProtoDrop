using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>Page result: the rows plus the opaque next cursor (null = last page).</summary>
/// <param name="Items">The items of the list my transfers result.</param>
/// <param name="NextCursor">The next cursor of the list my transfers result.</param>
public sealed record ListMyTransfersResult(IReadOnlyList<MyTransferRow> Items, string? NextCursor);
