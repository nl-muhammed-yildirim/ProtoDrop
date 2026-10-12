using System.Collections.Concurrent;
using wa.application.Ports;
using wa.domain;

namespace wa.application.Limits;

/// <summary>
/// 30 s TTL cache over <see cref="IPlansStore"/> (FR-053-4). Every plan read goes through here — no direct store calls outside the caches.
/// The host wires both caches with the same value from <c>Wa:Flags:TtlSeconds</c> (TA-3.4) — no literal in this class.
/// </summary>
public sealed class PlansCache(IPlansStore plans, TimeSpan ttl, Func<DateTimeOffset> clock)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <inheritdoc cref="IPlansStore.GetAsync"/> Propagates <see cref="PlanUnknownException"/> / DB failures untouched (AC-053-4, EC-053-3). Failures are not cached — the next read retries.
    public async Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(planCode, out var entry) && clock() - entry.FetchedAtUtc < ttl)
            return entry.Value; // AC-053-3: younger than TTL — same instance, no refetch.

        var plan = await plans.GetAsync(planCode, cancellationToken);
        _entries[planCode] = new Entry(clock(), plan);
        return plan;
    }

    private sealed record Entry(DateTimeOffset FetchedAtUtc, Plan Value);
}

/// <summary>
/// 30 s TTL cache over <see cref="IFlagsStore"/> (FR-053-4). Every flag read goes through here — no direct store calls outside the caches.
/// The host wires both caches with the same value from <c>Wa:Flags:TtlSeconds</c> (TA-3.4) — no literal in this class.
/// </summary>
public sealed class FlagsCache(IFlagsStore flags, TimeSpan ttl, Func<DateTimeOffset> clock)
{
    private Entry? _entry;

    /// <inheritdoc cref="IFlagsStore.GetAllAsync"/> Propagates DB failures untouched (EC-053-3). Failures are not cached — the next read retries.
    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
    {
        if (_entry is not null && clock() - _entry.FetchedAtUtc < ttl)
            return _entry.Value; // AC-053-3: younger than TTL — same instance, no refetch.

        var rows = await flags.GetAllAsync(cancellationToken);
        _entry = new Entry(clock(), rows);
        return rows;
    }

    private sealed record Entry(DateTimeOffset FetchedAtUtc, IReadOnlyList<FeatureFlag> Value);
}
