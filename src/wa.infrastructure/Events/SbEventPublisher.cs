using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.infrastructure.Events;

/// <summary>
/// Service Bus adapter for <see cref="IEventPublisher"/> (T-054-03). Publishes to topic <c>core</c> with
/// MessageId = EventId — broker-level dedup by that identity (FR-054-4, TA-5.1); the topic itself is created in
/// infra, not in code at M0. On publish failure the outbox row stays unpublished and PUBLISH_FAILED is logged
/// — no silent drop (EC-054-1). MVP simplification per TA-4.1.7: at-most-once accepted for non-critical events;
/// the outbox row + alert cover the rest. <b>Assumes a non-empty connection string</b>: Program.cs selects this adapter only when <c>Wa:ServiceBus:ConnectionString</c> is present, so the ctor input is guaranteed non-null in production (a direct construction with a missing key throws from the ServiceBusClient ctor at publish time — acceptable for M0).
/// </summary>
public sealed class SbEventPublisher : IEventPublisher
{
    private const string TopicName = "core";

    /// <summary>Publish timeout (TA-4.1.7).</summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    private readonly string _connectionString;
    private readonly ILogger<SbEventPublisher> _logger;
    private readonly IDbContextFactory<WaDbContext> _dbFactory;

    public SbEventPublisher(IConfiguration configuration, ILogger<SbEventPublisher> logger, IDbContextFactory<WaDbContext> dbFactory)
    {
        // Cached for publish-time use. DI selects this adapter only when the key exists (Program.cs), so production never sees a null here; direct constructions with an empty config throw at publish time from the ServiceBusClient ctor — OutboxRetryTests relies on that path.
        _connectionString = configuration["Wa:ServiceBus:ConnectionString"]!;
        _logger = logger;
        _dbFactory = dbFactory;
    }

    public async Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        // Serialize once — the outbox row and the wire payload share this exact byte shape (AC-054-2).
        var envelopeJson = JsonSerializer.Serialize(envelope);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var row = new DomainEvent
        {
            EventId = envelope.EventId,
            EventType = EventTypeConverter.WireName(envelope.Type), // TA-5.3 wire name — direct lookup, no serialization round-trip
            EnvelopeJson = envelopeJson,
            CorrelationId = envelope.CorrelationId,
            PartitionKey = envelope.PartitionKey,
            OccurredAtUtc = envelope.OccurredAtUtc,
            IsPublished = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Set<DomainEvent>().Add(row);
        await db.SaveChangesAsync(cancellationToken); // outbox row committed (same change tracker as business data once use cases exist)

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PublishTimeout);
            await using var client = new ServiceBusClient(_connectionString);
            await using var sender = client.CreateSender(TopicName);
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(envelopeJson)) { MessageId = envelope.EventId.ToString() };
            if (!string.IsNullOrEmpty(envelope.PartitionKey))
                message.PartitionKey = envelope.PartitionKey; // broker-side per-partition order (EC-054-2)
            await sender.SendMessageAsync(message, cancellationToken: cts.Token);

            row.IsPublished = true;
        }
        catch (Exception ex)
        {
            // Row stays unpublished — re-publish picks it up (TA-4.1.7). This log line is the M0 alert path.
            _logger.LogError(ex, "PUBLISH_FAILED eventId={EventId} eventType={EventType}", envelope.EventId, row.EventType);
        }

        await db.SaveChangesAsync(cancellationToken); // persist the publish flag; no-op save when it stayed false
        return new DomainEventRecord { EventId = envelope.EventId, IsPublished = row.IsPublished };
    }
}
