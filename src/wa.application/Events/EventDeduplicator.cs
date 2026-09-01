namespace wa.application.Events;

/// <summary>
/// T-006c consumer-side dedup by <c>eventId</c> (TA-5.2: "consumers **must**
/// be idempotent (dedup by eventId against a 15-min LRU)"). Service Bus is
/// at-least-once, so the same envelope can arrive more than once within its
/// 15-min retention. Callers guard the handler with <see cref="TryProcess"/>
/// and run the work only on <c>true</c> (first sighting) — that is the
/// TA-5.2 "idempotent" requirement, and this helper is exactly the "15-min
/// LRU" the doc names. Thread-safe; injectable clock.
/// </summary>
public sealed class EventDeduplicator
{
    private readonly object _sync = new();

    /// <summary>
    /// Recency-ordered LRU backbone. Head is the least-recently-seen entry
    /// (evict-first); tail is the most-recently-seen. Every duplicate
    /// sighting refreshes <see cref="SeenEntry.SeenAtUtc"/> and moves the
    /// node to the tail — so the 15-min window slides by <c>now</c> on
    /// repeat deliveries, not by first sighting.
    /// </summary>
    private readonly LinkedList<SeenEntry> _recency = new();

    /// <summary>
    /// eventId → its <see cref="_recency"/> node for O(1) duplicate
    /// lookup/refresh.
    /// </summary>
    private readonly Dictionary<string, LinkedListNode<SeenEntry>> _index = new();

    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _window;
    private readonly int _capacity;

    /// <summary>15-minute dedup window per TA-5.2 (frozen).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Default LRU capacity — a memory safety net, not a semantic bound.
    /// The real bound is the 15-min window; the cap protects a process
    /// that receives a pathological storm of distinct eventIds before
    /// eviction by age.
    /// </summary>
    public const int DefaultCapacity = 10_000;

    public EventDeduplicator(
        Func<DateTimeOffset>? now = null,
        TimeSpan? window = null,
        int capacity = DefaultCapacity)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _window = window ?? Window;
        _capacity = capacity > 0 ? capacity : DefaultCapacity;
    }

    /// <summary>
    /// Returns <c>true</c> if and only if this is the <b>first</b>
    /// sighting of <paramref name="eventId"/> in the 15-minute window
    /// (per TA-5.2 — the caller runs the handler). Subsequent sightings
    /// within <c>now + 15 min</c> return <c>false</c>; a sighting after
    /// the window closes returns <c>true</c> again.
    /// </summary>
    /// <param name="eventId">
    /// The envelope <c>eventId</c> — the Service Bus <c>MessageId</c> per
    /// TA-5.1 (dedup key = <c>eventId</c>).
    /// </param>
    public bool TryProcess(string eventId)
    {
        if (eventId is null)
        {
            throw new ArgumentNullException(nameof(eventId));
        }

        var now = _now();
        bool fresh;
        lock (_sync)
        {
            // Expire from the head: only LRU entries can still be stale.
            while (_recency.Count > 0)
            {
                var head = _recency.First!;
                if (now - head.Value.SeenAtUtc < _window)
                {
                    break;
                }
                _index.Remove(head.Value.Id);
                _recency.RemoveFirst();
            }

            if (_index.TryGetValue(eventId, out var existing))
            {
                // Duplicate within window: refresh timestamp + promote.
                existing!.Value.SeenAtUtc = now;
                _recency.Remove(existing);
                _recency.AddLast(existing);
                fresh = false;
            }
            else
            {
                var node = new LinkedListNode<SeenEntry>(new SeenEntry(eventId, now));
                _recency.AddLast(node);
                _index[eventId] = node;
                fresh = true;

                // LRU safety bound (memory cap, not the window).
                while (_recency.Count > _capacity)
                {
                    var head = _recency.First!;
                    _index.Remove(head.Value.Id);
                    _recency.RemoveFirst();
                }
            }
        }

        return fresh;
    }

    /// <summary>Number of distinct eventIds currently in the window (read-only).</summary>
    public int Depth
    {
        get { lock (_sync) { return _recency.Count; } }
    }

    /// <summary>One tracked eventId + the last time it was seen.</summary>
    private sealed class SeenEntry
    {
        public SeenEntry(string id, DateTimeOffset seenAtUtc)
        {
            Id = id;
            SeenAtUtc = seenAtUtc;
        }

        public string Id { get; set; } = default!;
        public DateTimeOffset SeenAtUtc { get; set; }
    }
}
