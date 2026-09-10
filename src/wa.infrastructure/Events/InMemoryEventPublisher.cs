namespace wa.infrastructure.Events;

/// <summary>
/// T-006c in-memory <see cref="IEventPublisher"/> fake (AGENT.md §5.2).
/// Registered when <c>Wa:ServiceBus:ConnectionString</c> is unset (local dev)
/// and in tests. It captures every published <see cref="EventEnvelope"/> so
/// tests can assert on exactly what was "sent", standing in for the
/// outbox-row + Service Bus publish of <see cref="SbEventPublisher"/>
/// (TA-5.2 / TA-5.1). No Service Bus, no EF — no packages (golden rule 1).
/// </summary>
public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly object _sync = new();

    /// <summary>Captured envelopes in publish order (thread-safe snapshot).</summary>
    private readonly List<EventEnvelope> _published = new();

    /// <summary>
    /// Read-only snapshot of every envelope published so far, in publish order.
    /// Use <see cref="Published"/> for assertions; <c>await</c> is the no-op
    /// contract of <see cref="IEventPublisher"/>.
    /// </summary>
    public IReadOnlyList<EventEnvelope> Published
    {
        get { lock (_sync) { return _published.ToList(); } }
    }

    /// <summary>Drops all captured envelopes (test teardown between cases).</summary>
    public void Clear()
    {
        lock (_sync) { _published.Clear(); }
    }

    /// <inheritdoc />
    public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrEmpty(envelope.EventId))
        {
            throw new ArgumentException("EventId must be set.", nameof(envelope));
        }

        lock (_sync)
        {
            // at-least-once mirror of SbEventPublisher: a redelivered eventId is
            // kept (the dedup by eventId happens consumer-side, TA-5.2).
            _published.Add(envelope);
        }

        return Task.CompletedTask;
    }
}