namespace wa.application.Ports;

/// <summary>Status of a single health-checked component.</summary>
public enum ComponentStatus
{
    Ok,
    Skipped,
}

/// <summary>Pings the configured database with a real SELECT 1 (no fakes).</summary>
public interface IDbHealthProbe
{
    /// <summary>Auto-creates the configured database from master if missing (dev-only), then runs SELECT 1. Throws when unreachable.</summary>
    Task PingAsync(CancellationToken cancellationToken);
}

/// <summary>Checks Service Bus reachability.</summary>
public interface ISbHealthProbe
{
    /// <summary>Returns Skipped when its connection string is unset; Ok when reachable. Throws when set but unreachable.</summary>
    Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken);
}
