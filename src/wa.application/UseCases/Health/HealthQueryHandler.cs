using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>Handles <see cref="HealthQuery"/>: pings the database and checks Service Bus, and reports <c>ok</c> or <c>skipped</c> per component. A failing probe throws, which the host turns into an unhealthy response.</summary>
public sealed partial class HealthQueryHandler : IRequestHandler<HealthQuery, HealthResult>
{
    private readonly IDbHealthProbe _db;
    private readonly ISbHealthProbe _sb;
    private readonly ILogger<HealthQueryHandler> _logger;

    /// <summary>Initializes a new instance of the <see cref="HealthQueryHandler"/> class.</summary>
    /// <param name="db">Probe that pings the database.</param>
    /// <param name="sb">Probe that checks Service Bus reachability.</param>
    /// <param name="logger">Logger for probe failures.</param>
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
            LogDatabaseUnreachable(_logger, ex);
            throw;
        }

        ComponentStatus sb;
        try
        {
            sb = await _sb.CheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            LogServiceBusUnreachable(_logger, ex);
            throw;
        }

        return new HealthResult("ok", "ok", sb == ComponentStatus.Skipped ? "skipped" : "ok");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Health check failed: database unreachable")]
    private static partial void LogDatabaseUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Health check failed: service bus unreachable")]
    private static partial void LogServiceBusUnreachable(ILogger logger, Exception exception);
}
