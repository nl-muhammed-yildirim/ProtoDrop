using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>Handles <see cref="HealthQuery"/> and produces <see cref="HealthResult"/>.</summary>
public sealed class HealthQueryHandler : IRequestHandler<HealthQuery, HealthResult>
{
    private readonly IDbHealthProbe _db;
    private readonly ISbHealthProbe _sb;
    private readonly ILogger<HealthQueryHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="HealthQueryHandler"/> class.</summary>
    /// <param name="db">The database.</param>
    /// <param name="sb">The Service Bus.</param>
    /// <param name="logger">The logger used for diagnostics.</param>
    public HealthQueryHandler(IDbHealthProbe db, ISbHealthProbe sb, ILogger<HealthQueryHandler> logger)
    {
        _db = db;
        _sb = sb;
        _logger = logger;
    }

    /// <inheritdoc />
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
