using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using wa.domain.Correlation;
using wa.domain.Errors;

namespace wa.api.Middleware;

/// <summary>
/// Serializes the exact TA-4.1.3 Problem+JSON field set — type, title, status, code, details, optional errors[], correlationId —
/// in that order (field order per the RFC 7804 example). Status/title are pinned by <see cref="ProblemCodes"/> so callers cannot drift;
/// correlationId comes from <see cref="CorrelationContext"/> (FR-056-1), never Activity.Current.
/// </summary>
public static class ProblemWriter
{
    /// <summary>TA-4.1.3 example format: base + lowercase machine code, e.g. <c>https://example.com/errors/transfer-size-exceeded</c>.</summary>
    public const string TypeBase = "https://example.com/errors/";

    /// <summary>Builds the Problem payload as a node tree with exact key order (JsonObject preserves insertion order).</summary>
    public static JsonObject Build(int status, string code, string details, IReadOnlyList<ProblemFieldError>? errors = null)
    {
        var payload = new JsonObject()
        {
            ["type"] = TypeBase + code.ToLowerInvariant(), // e.g. https://example.com/errors/transfer-size-exceeded — exact TA-4.1.3 example value
            ["title"] = ProblemCodes.TitleOf(code), // unknown codes fail fast here (ArgumentException at the call site)
            ["status"] = status,
            ["code"] = code,
            ["details"] = details
        };
        if (errors is not null) // optional TA-4.1.3 errors[] — present only for VALIDATION-shaped responses; key order stays exact either way
            payload["errors"] = new JsonArray(errors.Select(e => new JsonObject { ["field"] = e.Field, ["message"] = e.Message }).ToArray());
        payload["correlationId"] = CorrelationContext.CorrelationId; // first 8 hex of the trace-id — always present (middleware generates when absent)

        return payload;
    }

    /// <summary>
    /// Writes the Problem response and commits it before writing a single body byte (Response.StartAsync). Kestrel flushes headers with the first bytes, so any header set after this method returns is lost — callers that must carry extra headers (e.g. Retry-After) set them on <c>context.Response.Headers</c> beforehand.
    /// </summary>
    public static async Task WriteCompleteAsync(HttpContext context, int status, string code, string details, IReadOnlyList<ProblemFieldError>? errors = null)
    {
        var payload = Build(status, code, details, errors);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.StartAsync(); // commits headers — everything set on Response.Headers up to now is locked in
        await JsonSerializer.SerializeAsync(context.Response.Body, payload);
    }
    public static async Task WriteAsync(HttpContext context, int status, string code, string details, IReadOnlyList<ProblemFieldError>? errors = null)
    {
        var payload = Build(status, code, details, errors);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, payload);
    }
}
