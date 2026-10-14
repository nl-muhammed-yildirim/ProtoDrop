using System.Collections.Concurrent;
using System.Reflection;
using wa.application.Events;
using Xunit;

namespace wa.application.unit.Events;

// AC-054-3: a replay of eventId X within 15 minutes produces no repeated side effect (the consumer skips it).
// After expiry, re-delivery is handled by the consumer's own idempotency — this class proves only that the LRU hands the
// event back to the consumer once the window passes; "idempotent by design" is documented on EventDeduplicator.
public class EventDeduplicatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Replay_WithinWindow_IsSkipped() // AC-054-3.
    {
        var (dedup, clock) = CreateDedup();
        var eventId = Guid.NewGuid();

        Assert.False(dedup.AlreadySeen(eventId)); // first sight — process it.
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(dedup.AlreadySeen(eventId)); // replay within the 15-min window — skip, no side effect repeats.
    }

    [Fact]
    public void ReDelivery_AfterWindow_IsProcessedAgain() // post-expiry path → consumer idempotency (documented + unit-tested).
    {
        var (dedup, clock) = CreateDedup();
        var eventId = Guid.NewGuid();

        Assert.False(dedup.AlreadySeen(eventId));
        clock.Advance(EventDeduplicator.Window); // exactly 15 minutes — the window no longer covers the replay.
        Assert.False(dedup.AlreadySeen(eventId)); // handed back to the consumer; idempotency by design handles it (AC-054-3).
    }

    [Fact]
    public void DifferentEventIds_AreIndependent()
    {
        var (dedup, _) = CreateDedup();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid(); // EC-054-2: same partitionKey is fine — per-partition order stays broker-driven.

        Assert.False(dedup.AlreadySeen(first));
        Assert.False(dedup.AlreadySeen(second)); // unrelated event is not masked by the first one.
        Assert.True(dedup.AlreadySeen(first));
    }

    [Fact]
    public void ExpiredEntries_LeaveTheStore() // LRU eviction bound — no unbounded growth past the window.
    {
        var (dedup, clock) = CreateDedup();
        var first = Guid.NewGuid();

        dedup.AlreadySeen(first);
        Assert.Equal(1, Count(dedup));
        clock.Advance(EventDeduplicator.Window);
        dedup.AlreadySeen(Guid.NewGuid()); // the purge runs on a fresh event — expired entries are dropped.
        Assert.DoesNotContain(first, Ids(dedup));
    }
    private static ICollection<Guid> Ids(EventDeduplicator dedup) => Seen(dedup).Keys;
    private static (EventDeduplicator Dedup, FakeClock Clock) CreateDedup()
    {
        var clock = new FakeClock(Start);
        return (new EventDeduplicator(() => clock.Now), clock);
    }

    // Test-only reflection into the LRU's entry count / keys — no production surface needed.
    private static int Count(EventDeduplicator dedup) => Seen(dedup).Count;
    private static ConcurrentDictionary<Guid, DateTimeOffset> Seen(EventDeduplicator dedup)
        => (ConcurrentDictionary<Guid, DateTimeOffset>)typeof(EventDeduplicator)
            .GetField("_seen", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dedup)!;

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Advance(TimeSpan by) => Now += by;
    }
}
