using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>One page of the admin erasure report.</summary>
/// <param name="Items">Report rows of this page, newest deletion first.</param>
/// <param name="NextCursor">Cursor for the next page; null on the last page.</param>
public sealed record GetErasuresResult(IReadOnlyList<ErasureListItem> Items, string? NextCursor);
