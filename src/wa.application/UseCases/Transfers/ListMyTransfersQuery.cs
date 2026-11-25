using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's transfers, cursor-paged, most recent first.</summary>
/// <param name="OwnerId">Id of the signed-in user whose transfers are listed.</param>
/// <param name="Cursor">Opaque cursor from the previous page; null or empty for the first page.</param>
/// <param name="Limit">Maximum number of rows to return.</param>
/// <param name="Statuses">Status bytes to include; null for all non-deleted transfers.</param>
public sealed record ListMyTransfersQuery(Guid OwnerId, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
