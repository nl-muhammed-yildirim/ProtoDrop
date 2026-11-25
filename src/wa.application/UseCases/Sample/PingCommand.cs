using MediatR;

namespace wa.application.UseCases.Sample;

/// <summary>MediatR command for the ping use case.</summary>
public sealed record PingCommand : IRequest<string>;
