using MediatR;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

public sealed record HealthQuery : IRequest<HealthResult>;

/// <summary>The exact body of GET /health (AC-050-1). Property order is significant.</summary>
public sealed record HealthResult(string Status, string Db, string Sb);

public sealed class HealthQueryHandler : IRequestHandler<HealthQuery, HealthResult>
{
    private readonly IDbHealthProbe _db;
    private readonly ISbHealthProbe _sb;

    public HealthQueryHandler(IDbHealthProbe db, ISbHealthProbe sb)
    {
        _db = db;
        _sb = sb;
    }

    public async Task<HealthResult> Handle(HealthQuery request, CancellationToken cancellationToken)
    {
        await _db.PingAsync(cancellationToken);
        var sb = await _sb.CheckAsync(cancellationToken);
        return new HealthResult("ok", "ok", sb == ComponentStatus.Skipped ? "skipped" : "ok");
    }
}
