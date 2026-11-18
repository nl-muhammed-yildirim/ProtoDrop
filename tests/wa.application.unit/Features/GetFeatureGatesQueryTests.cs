using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Features;
using wa.domain;

namespace wa.application.unit.Features;

// US-044-02 (AC-044-2, EC-044-6): the public read of the six phase gates.
public class GetFeatureGatesQueryTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ReturnsExactlySixKeys_InGateOrder()
    {
        var result = await Run();

        Assert.Equal(FeatureGates.All, result.Keys.ToList());
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public async Task DataExportTrue_OnlyThatKeyOn()
    {
        var result = await Run(new FeatureFlag { Key = FeatureGates.DataExport, Value = "true" });

        Assert.True(result[FeatureGates.DataExport]);
        Assert.Equal(1, result.Values.Count(v => v));
    }

    [Fact]
    public async Task NoRows_AllOff() // EC-044-6
    {
        var result = await Run();

        Assert.All(result.Values, v => Assert.False(v));
    }

    [Fact]
    public async Task NonGateFlagRows_AreNotIncluded()
    {
        var result = await Run(new FeatureFlag { Key = "feature.resend", Value = "true" });

        Assert.False(result.ContainsKey("feature.resend"));
        Assert.Equal(6, result.Count);
    }

    private static async Task<IReadOnlyDictionary<string, bool>> Run(params FeatureFlag[] rows)
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new LimitsProvider(
            new PlansCache(new NoPlansStore(), Ttl, () => now),
            new FlagsCache(new StubFlagsStore(rows), Ttl, () => now));
        return await new GetFeatureGatesQueryHandler(provider).Handle(new GetFeatureGatesQuery(), CancellationToken.None);
    }

    private sealed class StubFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(rows);
    }

    private sealed class NoPlansStore : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => throw new PlanUnknownException(planCode);
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }
}
