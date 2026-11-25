namespace wa.application.Ports;

/// <summary>Pings the configured database with a real SELECT 1 (no fakes).</summary>
public interface IDbHealthProbe
{
    /// <summary>Auto-creates the configured database from master if missing (dev-only), then runs SELECT 1. Throws when unreachable.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task PingAsync(CancellationToken cancellationToken);
}
