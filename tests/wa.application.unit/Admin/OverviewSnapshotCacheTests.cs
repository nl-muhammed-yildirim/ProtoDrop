using wa.application.UseCases.Admin;

namespace wa.application.unit.Admin;

// T-011-03 (TA-4.2#20 / FR-011-6): 5-min TTL cache over IOverviewStore — mirrors Caches.cs idiom (single Entry? + injected clock).
// AC-011-1 server half: second call within 5 min returns the cached snapshot (same timestamp); staleness never exceeds 5 min.
// Hand stubs, no Moq (mirrors AuditWriterTests style).
public class OverviewSnapshotCacheTests
{
    private readonly StubOverviewStore _store;
    private readonly FakeClock _clock;
    private readonly OverviewSnapshotCache _cache;

    public OverviewSnapshotCacheTests()
    {
        _store = new StubOverviewStore();
        _clock = new FakeClock(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        _cache = new OverviewSnapshotCache(_store, TimeSpan.FromMinutes(5), () => _clock.Now);
    }

    // ---- Exit check 1: second call within 5 min → same snapshot (same timestamp), store called once (AC-011-1 cached half) ----

    [Fact]
    public async Task SecondCall_WithinTtl_ReturnsSameSnapshot_StoreCalledOnce()
    {
        var first = await _cache.GetAsync(CancellationToken.None);
        // Advance the clock 4 minutes — still within the 5-min TTL.
        _clock.Now += TimeSpan.FromMinutes(4);

        var second = await _cache.GetAsync(CancellationToken.None);

        Assert.Equal(1, _store.CallCount); // store called exactly once — the second read hit the cache.
        Assert.Equal(first.SnapshotAtUtc, second.SnapshotAtUtc); // same timestamp — the cached snapshot is returned verbatim.
        Assert.Equal(first.Data.ActiveTransfers, second.Data.ActiveTransfers);
        Assert.Equal(first.Data.StorageBytesUsed, second.Data.StorageBytesUsed);
        Assert.Equal(first.Data.NewUsersToday, second.Data.NewUsersToday);
        Assert.Equal(first.Data.EmailsSent7d, second.Data.EmailsSent7d);
        Assert.Equal(first.Data.EmailsFailed7d, second.Data.EmailsFailed7d);
    }

    // ---- Exit check 2: after ~5 min the snapshot refreshes (staleness never exceeds 5 min, FR-011-6) ----

    [Fact]
    public async Task Call_AfterFiveMinutes_Refetches_NewTimestamp()
    {
        var first = await _cache.GetAsync(CancellationToken.None);
        // Advance the clock exactly 5 minutes — the TTL has expired.
        _clock.Now += TimeSpan.FromMinutes(5);

        var second = await _cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, _store.CallCount); // store called twice — the second read refetched.
        Assert.NotEqual(first.SnapshotAtUtc, second.SnapshotAtUtc); // newer timestamp — the snapshot was recomputed.
    }

    // ---- Exit check 3: "new users today" is a UTC calendar day (US-011-01 edge case) ----

    [Fact]
    public async Task NewUsersTodayWindow_IsUtcCalendarDay()
    {
        // Clock at 23:59 UTC on day D — the store must capture todayStart == D T00:00Z and emailsWindowStart == now - 7d.
        var dayD = new DateTimeOffset(2026, 11, 4, 23, 59, 0, TimeSpan.Zero);
        _clock.Now = dayD;

        await _cache.GetAsync(CancellationToken.None);

        Assert.Equal(new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), _store.CapturedTodayStartUtc); // D T00:00Z
        Assert.Equal(dayD.UtcDateTime - TimeSpan.FromDays(7), _store.CapturedEmailsWindowStartUtc); // now - 7d
    }

    // ---- Exit check 4: snapshot fields pass through the store's values (four numbers + recent list shape) ----

    [Fact]
    public async Task Snapshot_FieldsPassThrough_StoreValues()
    {
        var recent = new List<RecentTransferItem>
        {
            new("abc12345", "ada@example.com", 1, 1024L, new DateTime(2026, 11, 4, 11, 0, 0, DateTimeKind.Utc)),
            new("def67890", null, 3, 2048L, new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc))
        };
        _store.Data = new OverviewData(42, 1048576L, 7, 15, 3, recent);

        var snapshot = await _cache.GetAsync(CancellationToken.None);

        Assert.Equal(42, snapshot.Data.ActiveTransfers);
        Assert.Equal(1048576L, snapshot.Data.StorageBytesUsed);
        Assert.Equal(7, snapshot.Data.NewUsersToday);
        Assert.Equal(15, snapshot.Data.EmailsSent7d);
        Assert.Equal(3, snapshot.Data.EmailsFailed7d);
        Assert.Equal(2, snapshot.Data.RecentTransfers.Count); // recent list shape preserved.
        Assert.Equal("abc12345", snapshot.Data.RecentTransfers[0].LinkId);
        Assert.Equal("ada@example.com", snapshot.Data.RecentTransfers[0].OwnerEmail);
        Assert.Equal(1, snapshot.Data.RecentTransfers[0].Status);
        Assert.Equal(1024L, snapshot.Data.RecentTransfers[0].TotalBytes);
    }

    // ---- Stubs (hand-written, no Moq — mirrors AuditWriterTests) ----

    private sealed class StubOverviewStore : IOverviewStore
    {
        public int CallCount { get; private set; }
        public DateTime CapturedTodayStartUtc { get; private set; }
        public DateTime CapturedEmailsWindowStartUtc { get; private set; }
        public OverviewData Data { get; set; } = new(0, 0L, 0, 0, 0, []);

        public Task<OverviewData> GetAsync(DateTime todayStartUtc, DateTime emailsWindowStartUtc, CancellationToken ct)
        {
            CallCount++;
            CapturedTodayStartUtc = todayStartUtc;
            CapturedEmailsWindowStartUtc = emailsWindowStartUtc;
            return Task.FromResult(Data);
        }
    }

    private sealed class FakeClock(DateTimeOffset initial)
    {
        public DateTimeOffset Now { get; set; } = initial;
    }
}
