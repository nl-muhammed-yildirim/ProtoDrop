using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>Result of the get erasures use case.</summary>
/// <param name="Items">The items of the get erasures result.</param>
/// <param name="NextCursor">The next cursor of the get erasures result.</param>
public sealed record GetErasuresResult(IReadOnlyList<ErasureListItem> Items, string? NextCursor);
