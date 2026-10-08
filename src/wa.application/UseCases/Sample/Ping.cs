using MediatR;

namespace wa.application.UseCases.Sample;

public sealed record PingCommand : IRequest<string>;

public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
{
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken) =>
        Task.FromResult("pong");
}
