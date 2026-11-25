using MediatR;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>MediatR query for the health use case.</summary>
public sealed record HealthQuery : IRequest<HealthResult>;
