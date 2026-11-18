using System.Text.Json;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using Xunit;

namespace wa.application.unit.Limits;

// US-020-01 / FR-020-1 / FR-020-4 / EC-020-3: one resolution site per request; guest → Free; kill-switch precedence inherited from FeaturesProvider.
public class PlanContextAccessorTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private const string FreeLimitsJson = """{"maxTransferSize":5368709120,"maxSingleFile":5368709120,"maxZipSize":4294967296,"retentionDays":7,"graceDays":3,"maxDownloads":100,"maxEmails":20,"storageQuota":5368709120,"activeTransfersMax":20}""";
    private const string ProLimitsJson = """{"maxTransferSize":10737418240,"maxSingleFile":10737418240,"maxZipSize":9223372036854775807,"retentionDays":30,"graceDays":3,"maxDownloads":1000,"maxEmails":100,"storageQuota":107374182400,"activeTransfersMax":200}""";
    private const string FreeFeaturesJson = """{"ads":false,"scheduling":false,"analytics":false,"branding":false,"sso_scim":false}""";
    private const string ProFeaturesJson = """{"ads":false,"scheduling":true,"analytics":true,"branding":true,"sso_scim":false}""";

    [Fact]
    public async Task AuthenticatedUser_ResolvesThatUsersPlan()
    {
        var userId = Guid.NewGuid();
        var (accessor, _, limits) = Create(users => users.Codes[userId] = "pro");
        accessor.Bind(userId);

        var context = await accessor.GetAsync();

        Assert.Equal("pro", context.PlanCode);
        var expected = await limits.Resolve("pro");
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(context.Limits));
        Assert.True(context.Features.Scheduling);
        Assert.True(context.Features.Analytics);
        Assert.True(context.Features.Branding);
        Assert.False(context.Features.SsoScim);
    }

    [Fact]
    public async Task Guest_ResolvesFree()
    {
        var (accessor, users, _) = Create();
        accessor.Bind(null);

        var context = await accessor.GetAsync();

        Assert.Equal(ILimitsProvider.GuestPlanCode, context.PlanCode);
        Assert.False(context.Features.Scheduling);
        Assert.Equal(0, users.Calls); // guests never touch the user store
    }

    [Fact]
    public async Task UnknownUserRow_ResolvesFree()
    {
        var (accessor, users, _) = Create();
        accessor.Bind(Guid.NewGuid());

        var context = await accessor.GetAsync();

        Assert.Equal(ILimitsProvider.GuestPlanCode, context.PlanCode);
        Assert.Equal(1, users.Calls);
    }

    [Fact]
    public async Task SoftDeletedUser_ResolvesLastKnownPlan() // EC-020-3: the store (no soft-delete filter) still returns the plan code.
    {
        var userId = Guid.NewGuid();
        var (accessor, _, _) = Create(users => users.Codes[userId] = "pro");
        accessor.Bind(userId);

        Assert.Equal("pro", (await accessor.GetAsync()).PlanCode);
    }

    [Fact]
    public async Task UnknownPlanCode_ThrowsPlanUnknownException() // D-6: stays 500 INTERNAL via GlobalErrorMiddleware.
    {
        var userId = Guid.NewGuid();
        var (accessor, _, _) = Create(users => users.Codes[userId] = "nope");
        accessor.Bind(userId);

        await Assert.ThrowsAsync<PlanUnknownException>(() => accessor.GetAsync());
    }

    [Fact]
    public async Task GlobalKillSwitch_OverridesPlanRow()
    {
        var userId = Guid.NewGuid();
        var flags = FeaturesProvider.GlobalGateKeys
            .Select(k => new FeatureFlag { Key = k, Value = k == "feature.scheduling" ? "false" : "true" })
            .ToList();
        var (accessor, _, _) = Create(users => users.Codes[userId] = "pro", flags);
        accessor.Bind(userId);

        var context = await accessor.GetAsync();

        Assert.False(context.Features.Scheduling);
        Assert.True(context.Features.Analytics);
    }

    [Fact]
    public async Task Free_GatedFeaturesUnavailable_EvenWithGlobalGatesOn() // US-020-04 story AC 1
    {
        var userId = Guid.NewGuid();
        var (accessor, _, _) = Create(users => users.Codes[userId] = "free", AllGatesOn());
        accessor.Bind(userId);

        var features = (await accessor.GetAsync()).Features;

        Assert.False(features.Scheduling);
        Assert.False(features.Branding);
        Assert.False(features.Analytics);
        Assert.False(features.Ads);
    }

    [Fact]
    public async Task Pro_GatedFeaturesAvailable_WhenGlobalGatesOn() // Pro row is ad-free (F-PRF-004)
    {
        var userId = Guid.NewGuid();
        var (accessor, _, _) = Create(users => users.Codes[userId] = "pro", AllGatesOn());
        accessor.Bind(userId);

        var features = (await accessor.GetAsync()).Features;

        Assert.True(features.Scheduling);
        Assert.True(features.Branding);
        Assert.True(features.Analytics);
        Assert.False(features.Ads);
    }

    [Fact]
    public async Task Upgrade_NextRequestSeesProFeatures() // US-020-04 story AC 2: user→plan read is uncached, so the next request sees the new plan.
    {
        var userId = Guid.NewGuid();
        var store = new FakeUserPlanStore();
        store.Codes[userId] = "free";
        var flags = AllGatesOn();

        var (first, _, _) = Create(flags: flags, sharedUsers: store);
        first.Bind(userId);
        Assert.False((await first.GetAsync()).Features.Scheduling);

        store.Codes[userId] = "pro";

        var (second, _, _) = Create(flags: flags, sharedUsers: store);
        second.Bind(userId);
        Assert.True((await second.GetAsync()).Features.Scheduling);
    }

    [Fact]
    public async Task SecondRead_SameInstance_StoreHitOnce()
    {
        var userId = Guid.NewGuid();
        var (accessor, users, _) = Create(u => u.Codes[userId] = "pro");
        accessor.Bind(userId);

        var first = await accessor.GetAsync();
        var second = await accessor.GetAsync();

        Assert.Same(first, second);
        Assert.Equal(1, users.Calls);
    }

    [Fact]
    public async Task FailedResolution_IsNotCached_RetryReResolves()
    {
        var userId = Guid.NewGuid();
        var (accessor, users, _) = Create(u => u.Codes[userId] = "nope");
        accessor.Bind(userId);

        await Assert.ThrowsAsync<PlanUnknownException>(() => accessor.GetAsync());
        users.Codes[userId] = "pro";

        Assert.Equal("pro", (await accessor.GetAsync()).PlanCode);
        Assert.Equal(2, users.Calls);
    }

    [Fact]
    public async Task GetBeforeBind_Throws()
    {
        var (accessor, _, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => accessor.GetAsync());
    }

    [Fact]
    public void BindTwice_Throws()
    {
        var (accessor, _, _) = Create();
        accessor.Bind(null);

        Assert.Throws<InvalidOperationException>(() => accessor.Bind(Guid.NewGuid()));
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static List<FeatureFlag> AllGatesOn() =>
        FeaturesProvider.GlobalGateKeys.Select(k => new FeatureFlag { Key = k, Value = "true" }).ToList();

    private static (PlanContextAccessor Accessor, FakeUserPlanStore Users, ILimitsProvider Limits) Create(
        Action<FakeUserPlanStore>? seed = null, IReadOnlyList<FeatureFlag>? flags = null, FakeUserPlanStore? sharedUsers = null)
    {
        var users = sharedUsers ?? new FakeUserPlanStore();
        seed?.Invoke(users);
        var now = DateTimeOffset.UtcNow;
        var plans = new PlansCache(new StubPlansStore(), Ttl, () => now);
        var flagsCache = new FlagsCache(new StubFlagsStore(flags ?? []), Ttl, () => now);
        var limits = new LimitsProvider(plans, flagsCache);
        var features = new FeaturesProvider(plans, flagsCache);
        return (new PlanContextAccessor(users, limits, features), users, limits);
    }

    private sealed class FakeUserPlanStore : IUserPlanStore
    {
        public Dictionary<Guid, string> Codes { get; } = new();
        public int Calls { get; private set; }

        public Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Codes.TryGetValue(userId, out var code) ? code : null);
        }
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => planCode switch
        {
            "free" => Task.FromResult(new Plan { Code = planCode, LimitsJson = FreeLimitsJson, FeaturesJson = FreeFeaturesJson }),
            "pro" => Task.FromResult(new Plan { Code = planCode, LimitsJson = ProLimitsJson, FeaturesJson = ProFeaturesJson }),
            _ => throw new PlanUnknownException(planCode),
        };

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }

    private sealed class StubFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(rows);
    }
}
