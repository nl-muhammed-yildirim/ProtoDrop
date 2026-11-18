using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-08 / US-044-01 (TA-4.2#23, FR-044-3): GET /api/v1/admin/flags — all seeded flag rows ordered by Key ascending,
// each with its type, plan default and overridden marker (effective-value view for the Limits group).
public class GetFlagsQueryTests
{
    private const string FreeLimitsJson = "{\"maxTransferSize\":5368709120,\"maxDownloads\":10}";

    private readonly StubIFlagsStore _flags;
    private readonly GetFlagsQueryHandler _handler;

    public GetFlagsQueryTests()
    {
        _flags = new StubIFlagsStore();
        var plans = new PlansCache(new StubIPlansStore(), TimeSpan.FromSeconds(30), () => DateTimeOffset.UtcNow);
        _handler = new GetFlagsQueryHandler(_flags, plans);
    }

    // ---- Exit check: GET lists all seeded keys, ordered by Key ascending ----

    [Fact]
    public async Task ReturnsAllRowsOrderedByKey()
    {
        // Stub rows unsorted — the handler must order them.
        _flags.Rows = new List<FeatureFlag>
        {
            new() { Key = "limits.pro.maxFiles", Value = "100" },
            new() { Key = "feature.ads", Value = "true" },
            new() { Key = "limits.free.maxTransferSize", Value = "5368709120" }
        };

        var result = await _handler.Handle(new GetFlagsQuery(), CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal("feature.ads", result[0].Key);
        Assert.Equal("limits.free.maxTransferSize", result[1].Key);
        Assert.Equal("limits.pro.maxFiles", result[2].Key);
    }

    // ---- Exit check: empty store → empty list, no throw ----

    [Fact]
    public async Task EmptyStore_ReturnsEmptyList()
    {
        _flags.Rows = new List<FeatureFlag>();

        var result = await _handler.Handle(new GetFlagsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    // ---- Effective value: row equal to the plan seed is not overridden ----

    [Fact]
    public async Task LimitEqualToSeed_NotOverridden_ReportsPlanDefaultAndIntType()
    {
        _flags.Rows = new List<FeatureFlag> { new() { Key = "limits.free.maxTransferSize", Value = "5368709120" } };

        var view = Assert.Single(await _handler.Handle(new GetFlagsQuery(), CancellationToken.None));

        Assert.Equal("int", view.Type);
        Assert.Equal("5368709120", view.PlanDefault);
        Assert.False(view.Overridden);
    }

    [Fact]
    public async Task LimitDifferentFromSeed_Overridden()
    {
        _flags.Rows = new List<FeatureFlag> { new() { Key = "limits.free.maxTransferSize", Value = "10737418240" } };

        var view = Assert.Single(await _handler.Handle(new GetFlagsQuery(), CancellationToken.None));

        Assert.Equal("10737418240", view.Value);
        Assert.Equal("5368709120", view.PlanDefault);
        Assert.True(view.Overridden);
    }

    [Fact]
    public async Task FeatureKey_IsBool_NoPlanDefault()
    {
        _flags.Rows = new List<FeatureFlag> { new() { Key = "feature.ads", Value = "true" } };

        var view = Assert.Single(await _handler.Handle(new GetFlagsQuery(), CancellationToken.None));

        Assert.Equal("bool", view.Type);
        Assert.Null(view.PlanDefault);
        Assert.False(view.Overridden);
    }

    [Fact]
    public async Task LimitForUnknownPlan_IntType_NoPlanDefault_NotOverridden()
    {
        _flags.Rows = new List<FeatureFlag> { new() { Key = "limits.gold.maxTransferSize", Value = "1" } };

        var view = Assert.Single(await _handler.Handle(new GetFlagsQuery(), CancellationToken.None));

        Assert.Equal("int", view.Type);
        Assert.Null(view.PlanDefault);
        Assert.False(view.Overridden);
    }

    // ---- US-044-02: description passes through (Features-group tooltip) ----

    [Fact]
    public async Task Description_PassesThrough()
    {
        _flags.Rows = new List<FeatureFlag>
        {
            new() { Key = "feature.search", Value = "false", Description = "Search global gate (F-XCT-002)" },
            new() { Key = "feature.ads", Value = "false" }
        };

        var result = await _handler.Handle(new GetFlagsQuery(), CancellationToken.None);

        Assert.Equal("Search global gate (F-XCT-002)", result.Single(v => v.Key == "feature.search").Description);
        Assert.Null(result.Single(v => v.Key == "feature.ads").Description);
    }

    // ---- Stubs (hand-written, no Moq) ----

    private sealed class StubIFlagsStore : IFlagsStore
    {
        public List<FeatureFlag> Rows { get; set; } = new();

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeatureFlag>>(Rows);
    }

    private sealed class StubIPlansStore : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            planCode == "free"
                ? Task.FromResult(new Plan { Code = "free", LimitsJson = FreeLimitsJson })
                : throw new PlanUnknownException(planCode);

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }
}
