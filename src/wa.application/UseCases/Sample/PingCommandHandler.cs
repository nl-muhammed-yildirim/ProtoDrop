using MediatR;

namespace wa.application.UseCases.Sample;

/// <summary>Handles <see cref="PingCommand"/> and produces <see cref="String"/>.</summary>
public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
{
    /// <inheritdoc />
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken) =>
        Task.FromResult("pong");
}
