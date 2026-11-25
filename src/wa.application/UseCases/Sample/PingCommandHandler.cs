using MediatR;

namespace wa.application.UseCases.Sample;

/// <summary>Handles <see cref="PingCommand"/> by returning the constant text <c>pong</c>.</summary>
public sealed class PingCommandHandler : IRequestHandler<PingCommand, string>
{
    /// <inheritdoc />
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken) =>
        Task.FromResult("pong");
}
