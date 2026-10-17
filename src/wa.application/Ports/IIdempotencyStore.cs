namespace wa.application.Ports;

/// <summary>A stored idempotency entry: the HTTP response to replay on duplicate requests (TA-4.1.5).</summary>
public sealed record IdempotencyEntry(string Key, string ResultJson, int StatusCode, DateTime ExpiresAtUtc);

/// <summary>
/// T-002-02 (TA-4.1.5): generic idempotency storage port — the middleware reads/writes HTTP response
/// bodies + status codes keyed by the client-supplied Idempotency-Key header. No transfer-specific logic.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct);
}
