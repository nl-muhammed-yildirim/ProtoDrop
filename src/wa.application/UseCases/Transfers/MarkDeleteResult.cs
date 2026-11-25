using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-04 (TA-5.3 / FR-009-3): the store's delete result — the MediatR contract type for this use case.</summary>
/// <param name="DeletedAtUtc">The deleted at UTC of the mark delete result.</param>
/// <param name="LinkId">Identifier of the link of the mark delete result.</param>
public sealed record MarkDeleteResult(DateTime DeletedAtUtc, string LinkId);
