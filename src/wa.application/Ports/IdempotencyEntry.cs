namespace wa.application.Ports;

/// <summary>A stored idempotency entry: the HTTP response to replay on duplicate requests (TA-4.1.5).</summary>
public sealed record IdempotencyEntry(string Key, string ResultJson, int StatusCode, DateTime ExpiresAtUtc);
