using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (TA-4.2#14 + q): endpoint 14 with a q parameter present. Same result type as the plain list (single wire site).</summary>
/// <param name="OwnerId">Identifier of the owner of the search my transfers query.</param>
/// <param name="Q">The q of the search my transfers query.</param>
/// <param name="Cursor">The cursor of the search my transfers query.</param>
/// <param name="Limit">The limit of the search my transfers query.</param>
/// <param name="Statuses">The statuses of the search my transfers query.</param>
public sealed record SearchMyTransfersQuery(Guid OwnerId, string? Q, string? Cursor, int Limit, IReadOnlyCollection<byte>? Statuses = null) : IRequest<ListMyTransfersResult>;
