namespace wa.application.Ports;

/// <summary>Checks Service Bus reachability.</summary>
public interface ISbHealthProbe
{
    /// <summary>Returns Skipped when its connection string is unset; Ok when reachable. Throws when set but unreachable.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The component status.</returns>
    Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken);
}
