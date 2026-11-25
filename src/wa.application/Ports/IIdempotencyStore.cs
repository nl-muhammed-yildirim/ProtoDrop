namespace wa.application.Ports;

/// <summary>
/// T-002-02 (TA-4.1.5): generic idempotency storage port — the middleware reads/writes HTTP response
/// bodies + status codes keyed by the client-supplied Idempotency-Key header. No transfer-specific logic.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Finds the idempotency entry.</summary>
    /// <param name="key">The key.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The idempotency entry, or <see langword="null"/> when there is none.</returns>
    Task<IdempotencyEntry?> FindAsync(string key, CancellationToken ct);
    /// <summary>Saves the operation.</summary>
    /// <param name="key">The key.</param>
    /// <param name="resultJson">The result JSON.</param>
    /// <param name="statusCode">The status code.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SaveAsync(string key, string resultJson, int statusCode, CancellationToken ct);
}
