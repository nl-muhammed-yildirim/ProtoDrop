using MediatR;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

public sealed record HealthQuery : IRequest<HealthResult>;
