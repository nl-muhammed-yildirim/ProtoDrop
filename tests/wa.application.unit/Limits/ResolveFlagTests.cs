using wa.application.Limits;
using wa.application.Ports;
using wa.domain;

namespace wa.application.unit.Limits;

// US-044-02 (FR-044-3, EC-044-6): ResolveFlag<T> reads one flag by key through FlagsCache; an absent row or an unreadable value yields the default (gates fail closed).
public class ResolveFlagTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AbsentKey_ReturnsDefault_Bool_And_Long() // EC-044-6
    {
        var (resolver, _) = Create();

        Assert.False(await resolver.ResolveFlag("feature.search", false));
        Assert.Equal(42L, await resolver.ResolveFlag("limits.free.maxFiles", 42L));
    }

    [Fact]
    public async Task LiteralTrueAndFalse_AreRead()
    {
        var (resolver, _) = Create(Flag("feature.search", "true"), Flag("feature.ads", "false"));

        Assert.True(await resolver.ResolveFlag("feature.search", false));
        Assert.False(await resolver.ResolveFlag("feature.ads", true));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData("null")]
    public async Task GarbledValue_ReturnsDefault_FailsClosed(string raw)
    {
        var (resolver, _) = Create(Flag("feature.search", raw));

        Assert.False(await resolver.ResolveFlag("feature.search", false));
    }

    [Fact]
    public async Task LimitRow_ReadAsLong()
    {
        var (resolver, _) = Create(Flag("limits.pro.maxTransferSize", "10737418240"));

        Assert.Equal(10_737_418_240L, await resolver.ResolveFlag("limits.pro.maxTransferSize", 0L));
    }

    [Fact]
    public async Task KeyMatch_IsOrdinal()
    {
        var (resolver, _) = Create(Flag("Feature.search", "true"));

        Assert.False(await resolver.ResolveFlag("feature.search", false));
    }

    [Fact]
    public async Task TwoCallsWithinTtl_HitTheStoreOnce() // reads go through FlagsCache
    {
        var (resolver, store) = Create(Flag("feature.search", "true"));

        await resolver.ResolveFlag("feature.search", false);
        await resolver.ResolveFlag("feature.ads", false);

        Assert.Equal(1, store.Calls);
    }

    private static (IFlagResolver Resolver, CountingFlagsStore Store) Create(params FeatureFlag[] rows)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new CountingFlagsStore(rows);
        var provider = new LimitsProvider(
            new PlansCache(new NoPlansStore(), Ttl, () => now),
            new FlagsCache(store, Ttl, () => now));
        return (provider, store);
    }

    private static FeatureFlag Flag(string key, string value) => new() { Key = key, Value = value };

    private sealed class CountingFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(rows);
        }
    }

    private sealed class NoPlansStore : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => throw new PlanUnknownException(planCode);
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }
}
