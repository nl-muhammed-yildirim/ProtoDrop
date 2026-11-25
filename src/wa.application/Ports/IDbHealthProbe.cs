namespace wa.application.Ports;

/// <summary>Pings the configured database with a real SELECT 1 (no fakes).</summary>
public interface IDbHealthProbe
{
    /// <summary>Runs a real <c>SELECT 1</c> against the configured database; in development it first creates the database from master if it is missing. Throws when the database is unreachable.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    Task PingAsync(CancellationToken cancellationToken);
}
