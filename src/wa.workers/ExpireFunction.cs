using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Billing;
using wa.application.UseCases.Transfers;
using wa.domain.Correlation;

namespace wa.workers;

/// <summary>
/// T-005-01: f-expire Azure Function — timer every 15 min, concurrency 1 (TA-6.2).
/// Claims its window via JobRun (TA-6.1) so scale-out instances don't double-process (FR-005-9).
/// T-005-04: HTTP trigger for E2E manual invocation.
/// US-022-04: also runs the schedule sub-step (Scheduled → Active + transfer.created re-emit) after expiry.
/// </summary>
public class ExpireFunction
{
    private readonly ExpireTransfersHandler _handler;
    private readonly ActivateScheduledTransfersHandler _scheduled;
    private readonly SendBillingEmailsHandler _billingEmails;
    private readonly EndBillingGraceHandler _billingGrace;
    private readonly ILogger<ExpireFunction> _logger;
    private readonly IConfiguration _config;

    /// <summary>Initializes a new instance of the <see cref="ExpireFunction"/> class.</summary>
    /// <param name="handler">Handler that marks overdue transfers expired.</param>
    /// <param name="scheduled">Handler that activates scheduled transfers that are due.</param>
    /// <param name="billingEmails">Handler that queues the payment-failed reminder emails.</param>
    /// <param name="billingGrace">Handler that downgrades users whose billing grace ended.</param>
    /// <param name="logger">Logger for the job run (carries the generated correlation ids).</param>
    /// <param name="config">Configuration source for the <c>Wa:Jobs:SignKey</c> that guards the HTTP trigger.</param>
    public ExpireFunction(
        ExpireTransfersHandler handler,
        ActivateScheduledTransfersHandler scheduled,
        SendBillingEmailsHandler billingEmails,
        EndBillingGraceHandler billingGrace,
        ILogger<ExpireFunction> logger,
        IConfiguration config)
    {
        _handler = handler;
        _scheduled = scheduled;
        _billingEmails = billingEmails;
        _billingGrace = billingGrace;
        _logger = logger;
        _config = config;
    }

    /// <summary>Timer entry point (every 15 minutes): expires overdue transfers, then runs the scheduled-send, billing-email and billing-grace sub-steps in that order inside one try block; an exception in any step is logged and rethrown, so the remaining steps are skipped until the next run.</summary>
    /// <param name="context">Function invocation context.</param>
    /// <param name="trigger">Timer trigger metadata (schedule status); not used.</param>
    [Function("Expire")]
    public async Task Run(
        FunctionContext context,
        [TimerTrigger("0 */15 * * * *")] TimerTriggerAttribute trigger)
    {
        // T-012-01: timer-triggered — no inbound header, so always generate a fresh valid traceparent (EC-056-1).
        var tp = W3CTraceParser.New();
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = tp.CorrelationId, ["TraceParent"] = tp.ToHeader() });

        var now = DateTime.UtcNow;
        try
        {
            var count = await _handler.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire completed: {Count} transfers expired", count);

            // US-022-04 (D-2): flip due Scheduled transfers to Active and re-emit transfer.created. After expiry so an
            // expired Scheduled row is swept first (AC-022-5); own JobRun claim.
            var activated = await _scheduled.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire schedule: {Count} transfers activated", activated);

            // US-019-04 (D-6): billing email sub-step; each send is gated by its own JobRun step claim.
            var billingCount = await _billingEmails.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire billing emails: {Count} queued", billingCount);

            // US-019-05 (D-4): end expired billing grace periods (downgrade to Free), after the email step.
            var graceCount = await _billingGrace.RunAsync(now, context.CancellationToken);
            _logger.LogInformation("f-expire billing grace: {Count} downgraded", graceCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "f-expire failed");
            throw; // Azure Functions retries on unhandled exception; JobRun claim prevents double-processing.
        }
    }

    /// <summary>T-005-04: HTTP trigger for E2E manual invocation (accelerates the 15-min timer).</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="req">The HTTP request; it must carry the <c>x-job-sign</c> header.</param>
    /// <returns>200 with the counts of the steps that ran, or a 401 problem when the signature is missing or wrong.</returns>
    [Function("ExpireHttp")]
    public async Task<HttpResponseData> RunHttp(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/expire")] HttpRequestData req)
    {
        if (!JobAuth.Verify(_config, req))
            return JobAuth.CreateProblem(req, 401, "Unauthenticated", "Invalid or missing signature.");

        var now = DateTime.UtcNow;
        var count = await _handler.RunAsync(now, context.CancellationToken);
        // US-022-04: same order as the timer — expiry first, then the schedule step (its own "schedule" JobRun claim).
        var activated = await _scheduled.RunAsync(now, context.CancellationToken);
        return JobAuth.CreateSuccess(req, new { expired = count, activated });
    }
}
