namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-03 (FR-011-6 / TA-4.2#20): 5-min TTL cache over <see cref="IOverviewStore"/> — mirrors Caches.cs exactly
/// (single Entry? + injected clock, no IMemoryCache). Platform-wide: one entry, no keying.
/// The host wires the TTL with TimeSpan.FromMinutes(5) — no literal in this class.
/// </summary>
/// <param name="store">Store that is queried on a cache miss.</param>
/// <param name="ttl">How long a computed snapshot stays valid.</param>
/// <param name="clock">Time source (injected for tests).</param>
public sealed class OverviewSnapshotCache(IOverviewStore store, TimeSpan ttl, Func<DateTimeOffset> clock)
{
    private Entry? _entry;

    /// <summary>Returns the cached snapshot, recomputing it once it is older than the TTL (AC-011-1: a second call within the TTL returns the same timestamp).</summary>
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
