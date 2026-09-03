using System.Text.Json;
using System.Text.Json.Nodes;

namespace wa.api.Pipeline;

/// <summary>
/// Serializes Problem+JSON payloads **exactly** as per TA-4.1.3:
/// camelCase keys, closed list <c>code</c>, <c>errors[]</c> when present,
/// and the <c>correlationId</c> (W3C trace-id, first 8 hex chars — TA-10.1).
/// </summary>
internal static class ProblemWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Write(
        int status,
        ErrorCode code,
        string correlationId,
        string type,
        string? details = null,
        IReadOnlyList<FieldError>? errors = null)
    {
        JsonObject root = new();
        root["type"] = type;
        root["title"] = BuildTitle(code);
        root["status"] = status;
        root["code"] = code.ToString();
        if (details is not null)
        {
            root["details"] = details;
        }
        if (errors is not null)
        {
            JsonArray errorArray = new();
            foreach (FieldError error in errors)
            {
                JsonObject entry = new();
                entry["field"] = error.Field;
                entry["message"] = error.Message;
                errorArray.Add(entry);
            }
            root["errors"] = errorArray;
        }
        root["correlationId"] = correlationId;
        // .NET 10: JsonObject is a JsonNode; serialize it explicitly to UTF-8 bytes.
        return JsonSerializer.SerializeToUtf8Bytes(root, SerializerOptions);
    }

    /// <summary>The wire value of the <c>type</c> field: <c>{apiBaseUrl}/errors/{kebab-case-code}</c></summary>
    public static string BuildTypeUri(string apiBaseUrl, ErrorCode code)
        => string.IsNullOrEmpty(apiBaseUrl)
            ? "errors/" + KebabCode(code)
            : apiBaseUrl.TrimEnd('/') + "/errors/" + KebabCode(code);

    /// <summary>Human-readable RFC 9457 <c>title</c> derived from the closed list code.</summary>
    public static string BuildTitle(ErrorCode code) => code switch
    {
        ErrorCode.VALIDATION => "Validation failed",
        ErrorCode.UNAUTHENTICATED => "Not authenticated",
        ErrorCode.FORBIDDEN => "Forbidden",
        ErrorCode.NOT_FOUND => "Not found",
        ErrorCode.TRANSFER_NOT_FINALIZED => "Transfer not finalized",
        ErrorCode.TRANSFER_SIZE_EXCEEDED => "Transfer size exceeded",
        ErrorCode.STORAGE_QUOTA_EXCEEDED => "Storage quota exceeded",
        ErrorCode.MAX_DOWNLOADS_REACHED => "Max downloads reached",
        ErrorCode.WRONG_PASSWORD => "Wrong password",
        ErrorCode.TRANSFER_EXPIRED => "Transfer expired",
        ErrorCode.TRANSFER_DELETED => "Transfer deleted",
        ErrorCode.FILES_GONE => "Files gone",
        ErrorCode.IDEMPOTENCY_CONFLICT => "Idempotency conflict",
        ErrorCode.RATE_LIMITED => "Rate limited",
        ErrorCode.STRIPE_WEBHOOK_MISMATCH => "Stripe webhook mismatch",
        ErrorCode.EMAIL_UNSUBSCRIBED => "Email unsubscribed",
        ErrorCode.INTERNAL => "Internal server error",
        _ => "Internal server error",
    };

    private static string KebabCode(ErrorCode code)
    {
        string[] parts = code.ToString().Split('_');
        return string.Join("-", parts.Select(s => s.ToLowerInvariant()));
    }
}
