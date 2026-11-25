using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Transfers;

/// <summary>T-009-04 (TA-5.3 / FR-009-3): the store's delete result — the MediatR contract type for this use case.</summary>
public sealed record MarkDeleteResult(DateTime DeletedAtUtc, string LinkId);
