using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Transfers;

namespace wa.workers;

/// <summary>
/// T-005-01: f-expire Azure Function — timer every 15 min, concurrency 1 (TA-6.2).
/// Claims its window via JobRun (TA-6.1) so scale-out instances don't double-process (FR-005-9).
/// </summary>
public class ExpireFunction
{
    private readonly ExpireTransfersHandler _handler;
    private readonly ILogger<ExpireFunction> _logger;

    public ExpireFunction(ExpireTransfersHandler handler, ILogger<ExpireFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("Expire")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 */15 * * * *")] TimerTriggerAttribute trigger)
    {
        var now = DateTime.UtcNow;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire completed: {Count} transfers expired", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-expire failed");
            throw; // Azure Functions retries on unhandled exception; JobRun claim prevents double-processing.
        }
    }
}
