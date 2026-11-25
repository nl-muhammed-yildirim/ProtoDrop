namespace wa.application.Ports;

/// <summary>Checks Service Bus reachability.</summary>
public interface ISbHealthProbe
{
    /// <summary>Returns Skipped when its connection string is unset; Ok when reachable. Throws when set but unreachable.</summary>
    Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken);
}
