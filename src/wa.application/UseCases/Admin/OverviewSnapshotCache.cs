namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-03 (FR-011-6 / TA-4.2#20): 5-min TTL cache over <see cref="IOverviewStore"/> — mirrors Caches.cs exactly
/// (single Entry? + injected clock, no IMemoryCache). Platform-wide: one entry, no keying.
/// The host wires the TTL with TimeSpan.FromMinutes(5) — no literal in this class.
/// </summary>
/// <param name="store">The store of the overview snapshot cache.</param>
/// <param name="ttl">The TTL of the overview snapshot cache.</param>
/// <param name="clock">The clock of the overview snapshot cache.</param>
public sealed class OverviewSnapshotCache(IOverviewStore store, TimeSpan ttl, Func<DateTimeOffset> clock)
{
    private Entry? _entry;

    /// <summary>
    /// AC-011-1: second call within 5 min returns the cached snapshot (same timestamp). Staleness never exceeds the TTL.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The overview snapshot.</returns>
    public async Task<OverviewSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (_entry is not null && clock() - _entry.FetchedAtUtc < ttl)
            return _entry.Value; // younger than TTL — same snapshot, no refetch.

        var nowUtc = clock();
        var todayStartUtc = nowUtc.Date; // UTC midnight of the current calendar day (US-011-01 edge case).
        var emailsWindowStartUtc = (nowUtc - TimeSpan.FromDays(7)).UtcDateTime;
        var data = await store.GetAsync(todayStartUtc, emailsWindowStartUtc, cancellationToken);
        _entry = new Entry(nowUtc, new OverviewSnapshot(data, nowUtc.UtcDateTime)); // SnapshotAtUtc set ONCE at computation time.
        return _entry.Value;
    }

    private sealed record Entry(DateTimeOffset FetchedAtUtc, OverviewSnapshot Value);
}
