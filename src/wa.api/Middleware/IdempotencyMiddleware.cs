using System.Text;
using wa.application.Ports;

namespace wa.api.Middleware;

/// <summary>
/// T-002-02 (TA-4.1.5, EC-002-1): generic HTTP idempotency middleware — protects POST endpoints
/// that accept an Idempotency-Key header. No transfer-specific logic: reads the header, checks
/// <see cref="IIdempotencyStore"/>, and on miss buffers the downstream response for replay.
///
/// Path matching (simple prefix/suffix — no router):
///   POST /api/v1/transfers/draft/{draftId}/finalize
///   POST /api/v1/transfers/{id}/send
///
/// Pipeline position: after IpRateLimiting, before GlobalErrorMiddleware — on replay the cached
/// response is written directly (bypassing error handling); on miss the full pipeline runs and
/// the resulting status + body are captured for future replays.
/// </summary>
/// <param name="next">Next middleware in the pipeline.</param>
/// <param name="store">Store that holds replayable responses by idempotency key.</param>
/// <param name="logger">Logger used only for the debug message written when a stored response is replayed (it logs the idempotency key and status code, never the body).</param>
/// <param name="timeProvider">Clock source; <see cref="TimeProvider.System"/> in production.</param>
public sealed partial class IdempotencyMiddleware(
    RequestDelegate next,
    IIdempotencyStore store,
    ILogger<IdempotencyMiddleware> logger,
    TimeProvider timeProvider)
{
    private readonly RequestDelegate _next = next;
    private readonly IIdempotencyStore _store = store;
    private readonly ILogger<IdempotencyMiddleware> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>For finalize and send POSTs that carry an <c>Idempotency-Key</c>, replays the stored response of an earlier identical request, or stores the response of this one when it succeeds.</summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Only POST requests on idempotency-protected paths.
        if (string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) is false)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (!IsProtectedPath(path))
        {
            await _next(context);
            return;
        }

        // No Idempotency-Key header → pass through (no protection for that request).
        var key = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key))
        {
            await _next(context);
            return;
        }

        // Check store: on hit (not expired) replay the stored response.
        var entry = await _store.FindAsync(key, context.RequestAborted);
        if (entry is not null && entry.ExpiresAtUtc > _timeProvider.GetUtcNow().UtcDateTime)
        {
            LogIdempotentReplay(_logger, key, entry.StatusCode);
            context.Response.StatusCode = entry.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(entry.ResultJson, Encoding.UTF8);
            return;
        }

        // Miss: buffer the downstream response so we can store it for future replays.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();

        context.Response.Body = buffer;
        await _next(context);
        context.Response.Body = originalBody;

        // Only cache successful responses (2xx) — errors are not idempotent-safe to replay.
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            buffer.Position = 0;
            var body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
            buffer.Position = 0;

            // Store for future replays (24 h TTL enforced in the store adapter).
            await _store.SaveAsync(key, body, context.Response.StatusCode, context.RequestAborted);

            // Write the buffered response to the client.
            await originalBody.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), context.RequestAborted);
        }
        else
        {
            // Non-2xx: write through without caching.
            buffer.Position = 0;
            await originalBody.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), context.RequestAborted);
        }
    }

    /// <summary>Matches the two idempotency-protected POST paths (TA-4.1.5).</summary>
    private static bool IsProtectedPath(string path)
    {
        // POST /api/v1/transfers/draft/{draftId}/finalize
        if (path.StartsWith("/api/v1/transfers/draft/", StringComparison.Ordinal) && path.EndsWith("/finalize", StringComparison.Ordinal))
            return true;

        // POST /api/v1/transfers/{id}/send
        if (path.StartsWith("/api/v1/transfers/", StringComparison.Ordinal) && path.EndsWith("/send", StringComparison.Ordinal))
            return true;

        return false;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Idempotency hit for key {Key} — replaying {Status}")]
    private static partial void LogIdempotentReplay(ILogger logger, string key, int status);
}
