using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.infrastructure.Events;

/// <summary>
/// T-006b Service Bus adapter for <see cref="IEventPublisher"/> (TA-5.2 / TA-5.1 / TA-4.1.7).
///
/// Flow (MVP simplification per TA-4.1.7 — "on failure: log, row stays for retry"):
///   1. Write the <see cref="EventOutbox"/> row keyed by <c>EventId</c>.
///   2. Publish the TA-5.2 envelope to the topic with <c>MessageId = eventId</c>
///      (the receiving side's dedup key, per TA-5.1) and the same partition key,
///      under a 10 s publish timeout.
///   3. On success, set <c>SentAtUtc</c>. On failure (<see cref="ServiceBusException"/>
///      or timeout), log <c>PUBLISH_FAILED</c>; the row stays with <c>SentAtUtc = null</c>.
///
/// When the <see cref="ServiceBusClient"/> is unavailable (local dev — empty
/// <c>Wa:ServiceBus:ConnectionString</c> per launchSettings / AGENT.md §5.2 "in-memory
/// fake locally"), publish is a no-op at Debug level; the outbox row still records
/// the event so retry can catch up.
/// </summary>
public sealed class SbEventPublisher : IEventPublisher
{
    private readonly WaDbContext _db;
    private readonly ServiceBusClient? _client;
    private readonly string _topic;
    private readonly ILogger<SbEventPublisher> _logger;

    /// <summary>Publish timeout per TA-4.1.7 (Service Bus publish timeout 10 s).</summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    public SbEventPublisher(
        WaDbContext db,
        ServiceBusClient? client,
        ServiceBusTopicOptions options,
        ILogger<SbEventPublisher> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _client = client;
        _topic = options.TopicName;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EventId is null)
        {
            throw new ArgumentException("EventId must be set.", nameof(envelope));
        }

        // Step 1 — outbox row (idempotent on redelivery; TA-3.2 unique index guards EventId).
        var existing = await _db.EventOutboxes
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.EventId == envelope.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.EventOutboxes.Add(new EventOutbox
            {
                Id = Guid.NewGuid(),
                EventId = envelope.EventId,
                EventType = envelope.EventType,
                PayloadJson = envelope.Payload is null
                    ? null
                    : JsonSerializer.Serialize(envelope.Payload),
                PartitionKey = envelope.PartitionKey,
                CorrelationId = envelope.CorrelationId,
                SentAtUtc = null, // NULL until publish succeeds — the retry signal
            });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogDebug(
                "Outbox row already present; re-publishing (at-least-once) eventId={EventId}",
                envelope.EventId);
        }

        // Step 2 — publish (TA-4.1.7: 10 s timeout; on failure the log line carries
        // PUBLISH_FAILED and the row stays for retry).
        try
        {
            await SendToServiceBusAsync(envelope, cancellationToken).ConfigureAwait(false);
            await MarkSentAsync(envelope.EventId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUBLISH_FAILED eventId={EventId} eventType={EventType}",
                envelope.EventId, envelope.EventType);
        }
    }

    private async Task SendToServiceBusAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            // Local dev with no Service Bus configured: the outbox row alone is the
            // record; an "in-memory fake" is a no-op publish (AGENT.md §5.2).
            _logger.LogDebug(
                "No ServiceBusClient configured; skipping publish for {EventType} (eventId={EventId})",
                envelope.EventType, envelope.EventId);
            return;
        }

        await using var sender = _client.CreateSender(_topic);
        using var timeout = new CancellationTokenSource(PublishTimeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var wire = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var message = new ServiceBusMessage
        {
            MessageId = envelope.EventId, // TA-5.1: dedup key = eventId
            CorrelationId = envelope.CorrelationId,
            PartitionKey = envelope.PartitionKey,
            Body = new BinaryData(wire),
        };
        await sender.SendMessageAsync(message, cts.Token).ConfigureAwait(false);
    }

    private async Task MarkSentAsync(string eventId, CancellationToken cancellationToken)
    {
        var row = await _db.EventOutboxes
            .FirstOrDefaultAsync(b => b.EventId == eventId, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            // Should not normally happen (we just wrote it); don't fail the publish.
            _logger.LogWarning("Outbox row missing at MarkSent eventId={EventId} — event marked unpublished",
                eventId);
            return;
        }

        if (row.SentAtUtc is null)
        {
            row.SentAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
