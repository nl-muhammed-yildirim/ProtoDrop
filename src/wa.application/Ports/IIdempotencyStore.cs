namespace wa.application.Ports;

/// <summary>
/// T-002-02 (TA-4.1.5): generic idempotency storage port — the middleware reads/writes HTTP response
/// bodies + status codes keyed by the client-supplied Idempotency-Key header. No transfer-specific logic.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct);
    Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct);
}
