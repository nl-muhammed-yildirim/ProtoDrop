using System.Collections.Concurrent;

namespace wa.application.Events;

/// <summary>
/// Consumer-side dedup (T-054-04): a 15-min LRU against <c>eventId</c> that consumers use to skip already-seen envelopes —
/// the broker's MessageId dedup is the first line, this is the second (FR-054-4). <b>The LRU is an optimization, not
/// the guarantee:</b> after <see cref="Window"/> expires a redelivery of the same eventId can still arrive, so every
/// consumer must also be idempotent by design (e.g. email send checks state, not just memory) — AC-054-3's post-expiry
/// path relies on that property.
/// </summary>
public sealed class EventDeduplicator(Func<DateTimeOffset> clock)
{
    /// <summary>Dedup window (FR-054-4/AC-054-3): replays of the same eventId inside this 15-min range are skipped.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _seen = new();

    /// <summary>Returns true if <paramref name="eventId"/> was already consumed within <see cref="Window"/> — the consumer skips it (AC-054-3).
    /// First sight (or re-sight after expiry) records the event and returns false. Different eventIds with the same partitionKey keep
    /// per-partition order via broker PartitionKey semantics (EC-054-2); consumers remain idempotent anyway.</summary>
    public bool AlreadySeen(Guid eventId)
    {
        var now = clock();
        if (_seen.TryGetValue(eventId, out var seenAt) && now - seenAt < Window)
            return true; // replay within the window — skip.

        _seen[eventId] = now;
        PurgeExpired(now); // bound memory: drop entries older than the window (LRU eviction).
        return false;
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        foreach (var entry in _seen)
            if (now - entry.Value >= Window)
                _seen.TryRemove(entry.Key, out _);
    }
}
