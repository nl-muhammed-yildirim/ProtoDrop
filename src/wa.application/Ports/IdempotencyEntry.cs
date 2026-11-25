namespace wa.application.Ports;

/// <summary>A stored idempotency entry: the HTTP response to replay on duplicate requests (TA-4.1.5).</summary>
/// <param name="Key">The key of the idempotency entry.</param>
/// <param name="ResultJson">The result JSON of the idempotency entry.</param>
/// <param name="StatusCode">The status code of the idempotency entry.</param>
/// <param name="ExpiresAtUtc">The expires at UTC of the idempotency entry.</param>
public sealed record IdempotencyEntry(string Key, string ResultJson, int StatusCode, DateTime ExpiresAtUtc);
