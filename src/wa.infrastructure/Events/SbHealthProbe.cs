using Microsoft.Extensions.Configuration;
using Azure.Messaging.ServiceBus;
using wa.application.Ports;

namespace wa.infrastructure.Events;

/// <summary>
/// Service Bus connectivity check. Skipped when its connection string is unset (SB is optional
/// locally — in-memory publisher fake used instead); throws when set but unreachable (EC-050-2).
/// </summary>
public sealed class SbHealthProbe : ISbHealthProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly string? _connectionString;

    public SbHealthProbe(IConfiguration configuration) =>
        _connectionString = configuration["Wa:ServiceBus:ConnectionString"];

    public async Task<ComponentStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            return ComponentStatus.Skipped;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ProbeTimeout);
        // $management is the AMQP management entity — reachable for any authed connection, no real queue needed.
        await using var client = new ServiceBusClient(_connectionString!);
        await using var receiver = client.CreateReceiver("$management");
        await receiver.PeekMessageAsync(cancellationToken: cts.Token);
        return ComponentStatus.Ok;
    }
}
