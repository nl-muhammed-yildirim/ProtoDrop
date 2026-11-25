namespace wa.application.Ports;

/// <summary>A stored idempotency entry: the HTTP response to replay on duplicate requests (TA-4.1.5).</summary>
/// <param name="Key">The client-supplied idempotency key.</param>
/// <param name="ResultJson">Serialized response body to replay.</param>
/// <param name="StatusCode">HTTP status code to replay.</param>
/// <param name="ExpiresAtUtc">UTC instant after which the entry is ignored (24 h after creation, TA-4.1.5).</param>
public sealed record IdempotencyEntry(string Key, string ResultJson, int StatusCode, DateTime ExpiresAtUtc);
