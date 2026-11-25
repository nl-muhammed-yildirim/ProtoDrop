using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's transfers, cursor-paged, most recent first.</summary>
/// <param name="OwnerId">Identifier of the owner of the list my transfers query.</param>
/// <param name="Cursor">The cursor of the list my transfers query.</param>
/// <param name="Limit">The limit of the list my transfers query.</param>
/// <param name="Statuses">The statuses of the list my transfers query.</param>
public sealed record ListMyTransfersQuery(Guid OwnerId, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
