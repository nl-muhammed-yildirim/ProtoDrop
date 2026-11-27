using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using wa.domain.Errors;
using wa.infrastructure.Email;
using wa.infrastructure.Events;

namespace wa.workers;

/// <summary>
/// CHORE-local-email: f-email-handoff — local-only HTTP trigger that receives the API's signed event POST and runs it through the
/// same <see cref="IEmailEnvelopeDispatcher"/> as the Service Bus trigger. It answers 404 outside Local/Development, so it is
/// invisible in production and the signing key is never read there.
/// </summary>
/// <param name="dispatcher">Shared dispatch of an event envelope to its email handler.</param>
/// <param name="config">Configuration source for the signing key and the host environment.</param>
public class EmailHandOffFunction(IEmailEnvelopeDispatcher dispatcher, IConfiguration config)
{
    private readonly IEmailEnvelopeDispatcher _dispatcher = dispatcher;
    private readonly IConfiguration _config = config;

    /// <summary>HTTP entry point <c>POST events/email</c>: verifies the <c>x-email-sign</c> HMAC over the exact body, then dispatches the envelope as the final delivery attempt (HTTP has no broker retry).</summary>
    /// <param name="context">Function invocation context; supplies the cancellation token.</param>
    /// <param name="req">The HTTP request whose body is the serialized TA-5.2 envelope.</param>
    /// <returns>200 when dispatched; 401 for a missing or bad signature; 404 outside Local/Development; 500 when the handler failed.</returns>
    [Function("f-email-handoff")]
    public async Task<HttpResponseData> Run(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = EmailHandOff.Route)] HttpRequestData req)
    {
        if (!EmailSenderSelector.IsLocalOrDevelopment(_config))
            return JobAuth.CreateProblem(req, 404, ProblemCodes.NotFound, "Not found.");

        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer, context.CancellationToken);
        var body = buffer.ToArray();

        // HttpHeadersCollection inherits BCL HttpHeaders: GetValues throws on absent headers, so use TryGetValues.
        req.Headers.TryGetValues(EmailHandOff.HeaderName, out var values);
        var provided = values is null ? null : string.Join(",", values);
        if (!EmailHandOff.Verify(body, provided, _config[EmailHandOff.SignKeyKey]))
            return JobAuth.CreateProblem(req, 401, ProblemCodes.Unauthenticated, "Invalid or missing signature.");

        try
        {
            await _dispatcher.DispatchAsync(Encoding.UTF8.GetString(body), EmailEnvelopeDispatcher.MaxDeliveryCount, context.CancellationToken);
        }
        catch (Exception)
        {
            // The dispatcher already logged the failure with the event id and attempt before rethrowing.
            return JobAuth.CreateProblem(req, 500, ProblemCodes.Internal, "Email dispatch failed.");
        }

        return JobAuth.CreateSuccess(req, new { dispatched = true });
    }
}
