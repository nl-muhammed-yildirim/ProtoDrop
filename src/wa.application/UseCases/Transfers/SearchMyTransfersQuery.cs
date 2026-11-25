using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (TA-4.2#14 + q): endpoint 14 with a q parameter present. Same result type as the plain list (single wire site).</summary>
public sealed record SearchMyTransfersQuery(Guid OwnerId, string? Q, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
