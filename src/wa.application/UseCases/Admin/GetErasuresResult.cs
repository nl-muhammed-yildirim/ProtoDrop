using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

public sealed record GetErasuresResult(IReadOnlyList<ErasureListItem> Items, string? NextCursor);
