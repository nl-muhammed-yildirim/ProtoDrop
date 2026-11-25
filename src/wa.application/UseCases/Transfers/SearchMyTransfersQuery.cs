using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (TA-4.2#14 + q): endpoint 14 with a q parameter present. Same result type as the plain list (single wire site).</summary>
/// <param name="OwnerId">Id of the signed-in user whose transfers are searched.</param>
/// <param name="Q">Raw search text; at most 100 characters after trimming.</param>
/// <param name="Cursor">Opaque cursor from the previous page; null or empty for the first page.</param>
/// <param name="Limit">Maximum number of rows to return.</param>
/// <param name="Statuses">Status bytes to include; null for all non-deleted transfers.</param>
public sealed record SearchMyTransfersQuery(Guid OwnerId, string? Q, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
