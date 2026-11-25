namespace wa.application.Ports;

/// <summary>
/// T-002-02 (TA-4.1.5): generic idempotency storage port — the middleware reads/writes HTTP response
/// bodies + status codes keyed by the client-supplied Idempotency-Key header. No transfer-specific logic.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Looks up the stored response for an idempotency key.</summary>
    /// <param name="key">Client-supplied <c>Idempotency-Key</c> header value.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The stored entry, or <see langword="null"/> when the key was never seen or has expired.</returns>
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct);
    /// <summary>Stores the response of a first request so that a repeated request with the same key can replay it.</summary>
    /// <param name="key">Client-supplied <c>Idempotency-Key</c> header value.</param>
    /// <param name="resultJson">Serialized response body to replay.</param>
    /// <param name="statusCode">HTTP status code to replay.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct);
}
