using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Transfers;

namespace wa.workers;

/// <summary>
/// T-005-03: f-delete-blobs Azure Function — timer hourly, concurrency 1 (TA-6.5).
/// Claims its window via JobRun (TA-6.1) so scale-out instances don't double-process (FR-005-9).
/// Host.json settings: concurrency 1, timeout 30 min.
/// </summary>
public class DeleteBlobsFunction
{
    private readonly DeleteOrphanBlobsHandler _handler;
    private readonly ILogger<DeleteBlobsFunction> _logger;

    public DeleteBlobsFunction(DeleteOrphanBlobsHandler handler, ILogger<DeleteBlobsFunction> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function("DeleteBlobs")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 0 * * * *")] TimerTriggerAttribute trigger)
    {
        var now = DateTime.UtcNow;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-delete-blobs completed: {Count} blobs deleted", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-delete-blobs failed");
            throw; // Azure Functions retries on unhandled exception; JobRun claim prevents double-processing.
        }
    }
}
