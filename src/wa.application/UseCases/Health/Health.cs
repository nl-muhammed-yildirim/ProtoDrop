using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
namespace wa.application.UseCases.Health;

public sealed record HealthQuery : IRequest<HealthResult>;

/// <summary>The exact body of GET /health (AC-050-1). Property order is significant.</summary>
public sealed record HealthResult(string Status, string Db, string Sb);

public sealed class HealthQueryHandler : IRequestHandler<HealthQuery, HealthResult>
{
    private readonly IDbHealthProbe _db;
    private readonly ISbHealthProbe _sb;
    private readonly ILogger<HealthQueryHandler> _logger;

    public HealthQueryHandler(IDbHealthProbe db, ISbHealthProbe sb, ILogger<HealthQueryHandler> logger)
    {
        _db = db;
        _sb = sb;
        _logger = logger;
    }

    public async Task<HealthResult> Handle(HealthQuery request, CancellationToken cancellationToken)
    {
        try
        {
            await _db.PingAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The endpoint maps this to 503 Problem+JSON (T-050-04); the reason must stay in the log line.
            _logger.LogError(ex, "Health check failed: database unreachable");
            throw;
        }

        ComponentStatus sb;
        try
        {
            sb = await _sb.CheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: service bus unreachable");
            throw;
        }

        return new HealthResult("ok", "ok", sb == ComponentStatus.Skipped ? "skipped" : "ok");
    }
}
