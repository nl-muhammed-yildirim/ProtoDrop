using System.Collections.Concurrent;
using System.Text.Json;
using wa.application.Ports;
using wa.domain;

namespace wa.infrastructure.Events;

/// <summary>
/// In-memory test double for <see cref="IEventPublisher"/> (T-054-05). Same contract as <see cref="SbEventPublisher"/>, same TA-5.2
/// serialized shape, and the same dedup-by-eventId behavior (publishing the same EventId twice yields one stored record — mirror of
/// broker-level MessageId dedup). Supports per-topic read-back so tests do publish → consume round-trips without a live broker (AC-054-4).
/// Expired entries are purged via an injectable clock — no unbounded growth, no real waits in tests.
/// This is a test double, not a second implementation: production code never branches on "is this the fake?" — DI picks the adapter (TA-0.2 rule 7).
/// </summary>
public sealed class InMemoryEventPublisher(TimeSpan ttl, Func<DateTimeOffset> clock) : IEventPublisher
{
    private const string TopicName = "core";

    private readonly ConcurrentDictionary<Guid, StoredEnvelope> _store = new();

    /// <summary>DI ctor (TA-0.2 rule 7): real clock, 30-min in-process retention — mirrors the broker's 30-day topic retention scaled down to a process-lifetime fake.</summary>
    public InMemoryEventPublisher() : this(TimeSpan.FromMinutes(30), () => DateTimeOffset.UtcNow) { }
    /// <inheritdoc cref="IEventPublisher.PublishAsync"/> Dedup: a second publish of the same EventId is a no-op (one stored record, no double side effect).
    public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope); // exact TA-5.2 byte shape — single source of truth (same as SbEventPublisher).
        var now = clock();

        if (_store.ContainsKey(envelope.EventId))
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true }); // already recorded — dedup.

        _store[envelope.EventId] = new StoredEnvelope(json, TopicName, now);
        PurgeExpired(now);
        return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
    }

    /// <summary>Returns stored envelopes for a topic in publish order (test round-trip support — AC-054-4 second Gherkin block).</summary>
    public IReadOnlyList<StoredEnvelope> GetByTopic(string topic)
    {
        PurgeExpired(clock());
        return _store.Values.Where(e => e.Topic == topic).OrderBy(e => e.StoredAtUtc).ToList();
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var entry in _store)
            if (now - entry.Value.StoredAtUtc >= ttl)
                _store.TryRemove(entry.Key, out _);
    }

    /// <summary>A single stored event: its exact serialized TA-5.2 envelope + metadata for read-back and TTL checks.</summary>
    public sealed record StoredEnvelope(string Json, string Topic, DateTimeOffset StoredAtUtc);
}
