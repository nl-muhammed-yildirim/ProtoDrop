using MediatR;

namespace wa.application.UseCases.Sample;

/// <summary>Sample command used by the pipeline smoke tests; it carries no data and is answered with <c>pong</c>.</summary>
public sealed record PingCommand : IRequest<string>;
