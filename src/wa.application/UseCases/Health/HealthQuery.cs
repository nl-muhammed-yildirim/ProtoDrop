using MediatR;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>Query for <c>GET /health</c>; carries no data.</summary>
public sealed record HealthQuery : IRequest<HealthResult>;
