using MediatR;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-01 (TA-4.2#14): GET /api/v1/transfers — the session user's transfers, cursor-paged, most recent first.</summary>
public sealed record ListMyTransfersQuery(Guid OwnerId, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
