using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using Xunit;

namespace wa.application.unit.Limits;

// AC-053-3: a cached entry younger than the TTL is served from cache (no refetch); after the TTL the next read refetches.
// The 30 s literal lives here, not in the caches — production wires both with Wa:Flags:TtlSeconds (TA-3.4).
public class CachesTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    // ── PlansCache ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Plans_Get_YoungerThanTtl_ReturnsCachedInstanceWithoutRefetch() // AC-053-3.
    {
        var (cache, store, _) = CreatePlans();

        var first = await cache.GetAsync("free", CancellationToken.None);
        var second = await cache.GetAsync("free", CancellationToken.None);

        Assert.Same(first, second); // younger than the TTL — same instance from cache.
        Assert.Equal(1, store.Calls); // no refetch.
    }
    [Fact]
    public async Task Plans_Get_DifferentPlans_AreCachedIndependently()
    {
        var (cache, store, _) = CreatePlans();

        await cache.GetAsync("free", CancellationToken.None);
        await cache.GetAsync("pro", CancellationToken.None);

        Assert.Equal(2, store.Calls); // one fetch per plan code.
    }

    [Fact]
    public async Task Plans_Get_AfterTtl_AdvancingClock_Refetches()
    {
        var (cache, store, clock) = CreatePlans();

        await cache.GetAsync("free", CancellationToken.None);
        Assert.Equal(1, store.Calls);

        clock.Advance(Ttl + TimeSpan.FromSeconds(1)); // age > TTL.
        await cache.GetAsync("free", CancellationToken.None);

        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task Plans_Get_StoreFailure_PropagatesAndIsNotCached()
    {
        var (cache, store, _) = CreatePlans();
        store.Throw = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("free", CancellationToken.None));
        Assert.Equal(1, store.Calls); // failure surfaced, nothing cached.

        store.Throw = null; // store recovers — next read fetches again (no poison entry).
        await cache.GetAsync("free", CancellationToken.None);

        Assert.Equal(2, store.Calls);
    }

    // ── FlagsCache ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Flags_GetAll_YoungerThanTtl_ReturnsCachedListWithoutRefetch() // AC-053-3.
    {
        var (cache, store, _) = CreateFlags();
        var first = await cache.GetAllAsync(CancellationToken.None);
        var second = await cache.GetAllAsync(CancellationToken.None);

        Assert.Same(first, second); // younger than the TTL — same instance from cache.
        Assert.Equal(1, store.Calls); // no refetch.
    }

    [Fact]
    public async Task Flags_GetAll_AfterTtl_AdvancingClock_Refetches()
    {
        var (cache, store, clock) = CreateFlags();

        await cache.GetAllAsync(CancellationToken.None);
        Assert.Equal(1, store.Calls);

        clock.Advance(Ttl + TimeSpan.FromSeconds(1)); // age > TTL.
        await cache.GetAllAsync(CancellationToken.None);

        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task Flags_GetAll_StoreFailure_PropagatesAndIsNotCached()
    {
        var (cache, store, _) = CreateFlags();
        store.Throw = new InvalidOperationException("db down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAllAsync(CancellationToken.None));
        Assert.Equal(1, store.Calls); // failure surfaced, nothing cached.

        store.Throw = null; // store recovers — next read fetches again (no poison entry).
        await cache.GetAllAsync(CancellationToken.None);

        Assert.Equal(2, store.Calls);
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static (PlansCache Cache, StubPlansStore Store, FakeClock Clock) CreatePlans()
    {
        var store = new StubPlansStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        return (new PlansCache(store, Ttl, () => clock.Now), store, clock);
    }

    private static (FlagsCache Cache, StubFlagsStore Store, FakeClock Clock) CreateFlags()
    {
        var store = new StubFlagsStore();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        return (new FlagsCache(store, Ttl, () => clock.Now), store, clock);
    }

    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Plan? Plan { get; set; } = new();
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw is not null) return Task.FromException<Plan>(Throw);
            return Task.FromResult(Plan!);
        }
    }

    private sealed class StubFlagsStore : IFlagsStore
    {
        public IReadOnlyList<FeatureFlag> Rows { get; set; } = new List<FeatureFlag>();
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw is not null) return Task.FromException<IReadOnlyList<FeatureFlag>>(Throw);
            return Task.FromResult(Rows);
        }
    }
}
