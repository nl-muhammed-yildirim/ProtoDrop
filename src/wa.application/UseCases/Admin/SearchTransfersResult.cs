namespace wa.application.UseCases.Admin;

/// <summary>T-011-04: flat result — items + nextCursor (null when no next page).</summary>
/// <param name="Items">Transfer rows of this page, newest first.</param>
/// <param name="NextCursor">Cursor for the next page; null when this is the last page.</param>
public record SearchTransfersResult(
    IReadOnlyList<AdminTransferRow> Items,
    string? NextCursor);
