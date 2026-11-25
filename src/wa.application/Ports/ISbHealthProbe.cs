namespace wa.application.Ports;

/// <summary>Checks Service Bus reachability.</summary>
public interface ISbHealthProbe
{
    /// <summary>Returns Skipped when its connection string is unset; Ok when reachable. Throws when set but unreachable.</summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns><see cref="ComponentStatus.Ok"/> when reachable or <see cref="ComponentStatus.Skipped"/> when Service Bus is not configured.</returns>
    Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken);
}
