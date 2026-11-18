using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using Xunit;

namespace wa.application.unit.Limits;

// US-018-05 / FR-018-1 / TA-13.2: effective feature = plan row AND global feature.* gate; gate only switches off.
public class FeaturesProviderTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    // Part-2 seeds, verbatim from PlanConfiguration.
    private const string FreeFeaturesJson = """{"ads":false,"scheduling":false,"analytics":false,"branding":false,"sso_scim":false}""";
    private const string ProFeaturesJson = """{"ads":false,"scheduling":true,"analytics":true,"branding":true,"sso_scim":false}""";
    private const string BusinessFeaturesJson = """{"ads":false,"scheduling":true,"analytics":true,"branding":true,"sso_scim":true}""";

    [Fact]
    public async Task Resolve_GatesOn_ReturnsPlanRowValues_PerPlan()
    {
        var provider = CreateProvider(AllGates("true"));

        AssertFeatures(await provider.Resolve("free"), ads: false, scheduling: false, analytics: false, branding: false, ssoScim: false);
        AssertFeatures(await provider.Resolve("pro"), ads: false, scheduling: true, analytics: true, branding: true, ssoScim: false);
        AssertFeatures(await provider.Resolve("business"), ads: false, scheduling: true, analytics: true, branding: true, ssoScim: true);
    }

    [Fact]
    public async Task Resolve_KillSwitchOff_DisablesForEveryPlan()
    {
        var rows = AllGates("true").Where(r => r.Key != "feature.scheduling").Append(Flag("feature.scheduling", "false")).ToList();
        var provider = CreateProvider(rows);

        AssertFeatures(await provider.Resolve("pro"), ads: false, scheduling: false, analytics: true, branding: true, ssoScim: false);
        AssertFeatures(await provider.Resolve("business"), ads: false, scheduling: false, analytics: true, branding: true, ssoScim: true);
    }

    [Fact]
    public async Task Resolve_GateOn_NeverGrantsWhatPlanRowLacks()
    {
        var provider = CreateProvider(AllGates("true"));

        AssertFeatures(await provider.Resolve("free"), ads: false, scheduling: false, analytics: false, branding: false, ssoScim: false);
    }

    [Fact]
    public async Task Resolve_SeededDefaults_AllGatesFalse_EveryPlanAllOff()
    {
        var provider = CreateProvider(AllGates("false"));

        foreach (var code in new[] { "free", "pro", "business" })
            AssertFeatures(await provider.Resolve(code), false, false, false, false, false);
    }

    [Fact]
    public async Task Resolve_GateRowAbsent_PlanRowDecides()
    {
        var provider = CreateProvider(Array.Empty<FeatureFlag>());

        AssertFeatures(await provider.Resolve("business"), ads: false, scheduling: true, analytics: true, branding: true, ssoScim: true);
    }

    [Fact]
    public async Task Resolve_GateValueUnparseable_TreatedAsOff()
    {
        var rows = AllGates("true").Where(r => r.Key != "feature.analytics").Append(Flag("feature.analytics", "yes")).ToList();
        var provider = CreateProvider(rows);

        AssertFeatures(await provider.Resolve("pro"), ads: false, scheduling: true, analytics: false, branding: true, ssoScim: false);
    }

    [Fact]
    public async Task Resolve_LimitsPrefixedFeatureKeys_Ignored() // D-2 scope pin: only FeaturesJson + feature.* gates.
    {
        var rows = AllGates("true").Append(Flag("limits.pro.scheduling", "false")).ToList();
        var provider = CreateProvider(rows);

        Assert.True((await provider.Resolve("pro")).Scheduling);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Resolve_NullOrBlankPlanCode_ResolvesDefaultPlan(string? planCode)
    {
        var provider = CreateProvider(AllGates("true"));

        var features = await provider.Resolve(planCode);
        var free = await provider.Resolve(Plan.DefaultCode);

        AssertFeatures(features, free.Ads, free.Scheduling, free.Analytics, free.Branding, free.SsoScim);
    }

    [Fact]
    public async Task Resolve_UnknownPlan_ThrowsPlanUnknownException()
    {
        var provider = CreateProvider(AllGates("true"));

        await Assert.ThrowsAsync<PlanUnknownException>(() => provider.Resolve("nope"));
    }

    [Fact]
    public async Task Resolve_RowOrGateEdited_VisibleOnlyAfterTtl()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var planStore = new StubPlansStore();
        var flagStore = new StubFlagsStore(AllGates("true"));
        var provider = new FeaturesProvider(
            new PlansCache(planStore, Ttl, () => clock.Now),
            new FlagsCache(flagStore, Ttl, () => clock.Now));

        var first = await provider.Resolve("pro");
        Assert.True(first.Scheduling);
        Assert.True(first.Analytics);

        planStore.Json["pro"] = FreeFeaturesJson;
        flagStore.Rows = AllGates("true").Where(r => r.Key != "feature.analytics").Append(Flag("feature.analytics", "false")).ToList();

        clock.Advance(TimeSpan.FromSeconds(29));
        var within = await provider.Resolve("pro");
        Assert.True(within.Scheduling);
        Assert.True(within.Analytics);

        clock.Advance(TimeSpan.FromSeconds(2));
        var after = await provider.Resolve("pro");
        Assert.False(after.Scheduling);
        Assert.False(after.Analytics);
    }

    [Fact]
    public async Task Resolve_ReturnsFreshInstance()
    {
        var provider = CreateProvider(AllGates("true"));

        var first = await provider.Resolve("pro");
        first.Scheduling = false;
        var second = await provider.Resolve("pro");

        Assert.True(second.Scheduling);
    }

    [Fact]
    public void GlobalGateKeys_AreTheFiveTa132Names()
    {
        Assert.Equal(
            new[] { "feature.ads", "feature.analytics", "feature.branding", "feature.scheduling", "feature.ssoScim" },
            FeaturesProvider.GlobalGateKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static void AssertFeatures(PlanFeatures f, bool ads, bool scheduling, bool analytics, bool branding, bool ssoScim)
    {
        Assert.Equal(ads, f.Ads);
        Assert.Equal(scheduling, f.Scheduling);
        Assert.Equal(analytics, f.Analytics);
        Assert.Equal(branding, f.Branding);
        Assert.Equal(ssoScim, f.SsoScim);
    }

    private static List<FeatureFlag> AllGates(string value) =>
        FeaturesProvider.GlobalGateKeys.Select(k => Flag(k, value)).ToList();

    private static FeaturesProvider CreateProvider(IReadOnlyList<FeatureFlag> rows)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        return new FeaturesProvider(
            new PlansCache(new StubPlansStore(), Ttl, () => clock.Now),
            new FlagsCache(new StubFlagsStore(rows), Ttl, () => clock.Now));
    }

    private static FeatureFlag Flag(string key, string value) => new() { Key = key, Value = value };

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Dictionary<string, string> Json { get; } = new()
        {
            ["free"] = FreeFeaturesJson,
            ["pro"] = ProFeaturesJson,
            ["business"] = BusinessFeaturesJson,
        };

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
        {
            if (!Json.TryGetValue(planCode, out var json)) throw new PlanUnknownException(planCode);
            return Task.FromResult(new Plan { Code = planCode, FeaturesJson = json });
        }

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }

    private sealed class StubFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public IReadOnlyList<FeatureFlag> Rows { get; set; } = rows;

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Rows);
    }
}
