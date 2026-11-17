using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using Xunit;

namespace wa.application.unit.Limits;

// AC-053-1/2/4 + FR-053-6/EC-053-1: Resolve returns each plan's exact Part-2 seed values, applies only matching limits.<plan>.<field> overrides to one field, and surfaces PlanUnknownException for unknown codes. Guest = caller passes ILimitsProvider.GuestPlanCode — no account parameter or lookup exists on the signature (FR-053-6).
public class LimitsProviderTests
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    // Part-2 constants, verbatim from the seed (T-052-04) — AC-053-1.
    private const string FreeLimitsJson = """{"maxTransferSize":5368709120,"maxSingleFile":5368709120,"maxZipSize":4294967296,"retentionDays":7,"graceDays":3,"maxDownloads":100,"maxEmails":20,"storageQuota":5368709120,"activeTransfersMax":20}""";
    private const string ProLimitsJson = """{"maxTransferSize":10737418240,"maxSingleFile":10737418240,"maxZipSize":9223372036854775807,"retentionDays":30,"graceDays":3,"maxDownloads":1000,"maxEmails":100,"storageQuota":107374182400,"activeTransfersMax":200}""";
    private const string BusinessLimitsJson = """{"maxTransferSize":107374182400,"maxSingleFile":107374182400,"maxZipSize":9223372036854775807,"retentionDays":90,"graceDays":3,"maxDownloads":-1,"maxEmails":500,"storageQuota":1099511627776,"activeTransfersMax":-1}""";

    [Fact]
    public async Task Resolve_NoFlags_ReturnsExactSeedValues_PerPlan() // AC-053-1.
    {
        var provider = CreateProvider();

        var free = await provider.Resolve("free", CancellationToken.None);
        Assert.Equal(5_368_709_120L, free.MaxTransferSize);
        Assert.Equal(5_368_709_120L, free.MaxSingleFile);
        Assert.Equal(4_294_967_296L, free.MaxZipSize);
        Assert.Equal(7, free.RetentionDays);
        Assert.Equal(3, free.GraceDays);
        Assert.Equal(100, free.MaxDownloads);
        Assert.Equal(20, free.MaxEmails);
        Assert.Equal(5_368_709_120L, free.StorageQuota);
        Assert.Equal(20, free.ActiveTransfersMax);

        var pro = await provider.Resolve("pro", CancellationToken.None);
        Assert.Equal(10_737_418_240L, pro.MaxTransferSize);
        Assert.Equal(10_737_418_240L, pro.MaxSingleFile);
        Assert.Equal(long.MaxValue, pro.MaxZipSize);
        Assert.Equal(30, pro.RetentionDays);
        Assert.Equal(3, pro.GraceDays);
        Assert.Equal(1000, pro.MaxDownloads);
        Assert.Equal(100, pro.MaxEmails);
        Assert.Equal(107_374_182_400L, pro.StorageQuota);
        Assert.Equal(200, pro.ActiveTransfersMax);

        var business = await provider.Resolve("business", CancellationToken.None);
        Assert.Equal(107_374_182_400L, business.MaxTransferSize);
        Assert.Equal(107_374_182_400L, business.MaxSingleFile);
        Assert.Equal(long.MaxValue, business.MaxZipSize);
        Assert.Equal(90, business.RetentionDays);
        Assert.Equal(3, business.GraceDays);
        Assert.Equal(-1, business.MaxDownloads); // -1 = unlimited (TA-4.2).
        Assert.Equal(500, business.MaxEmails);
        Assert.Equal(1_099_511_627_776L, business.StorageQuota);
        Assert.Equal(-1, business.ActiveTransfersMax);
    }

    [Fact]
    public async Task Resolve_GuestPlanCode_ReturnsFreeLimitsWithoutAccountLookup() // FR-053-6: the signature takes only a plan code — no account parameter to look up.
    {
        var provider = CreateProvider();

        LimitsRecord guest = await provider.Resolve(ILimitsProvider.GuestPlanCode, CancellationToken.None);

        Assert.Equal(5_368_709_120L, guest.MaxTransferSize); // Free's seed — no flag rows were even present.
    }

    [Fact]
    public async Task Resolve_FlagOverride_ChangesOnlyThatField_OnThatPlan() // AC-053-2: asserted field-by-field — exactly one field changed, on exactly one plan (T-053-06).
    {
        var provider = CreateProvider(rows: new[]
        {
            Flag("limits.free.maxTransferSize", "10737418240"), // 10 GB.
        });

        var free = await provider.Resolve("free", CancellationToken.None);
        Assert.Equal(10_737_418_240L, free.MaxTransferSize);  // the override — only this field of free moves.
        Assert.Equal(5_368_709_120L, free.MaxSingleFile);
        Assert.Equal(4_294_967_296L, free.MaxZipSize);
        Assert.Equal(7, free.RetentionDays);
        Assert.Equal(3, free.GraceDays);
        Assert.Equal(100, free.MaxDownloads);
        Assert.Equal(20, free.MaxEmails);
        Assert.Equal(5_368_709_120L, free.StorageQuota);
        Assert.Equal(20, free.ActiveTransfersMax);

        var pro = await provider.Resolve("pro", CancellationToken.None); // other plans completely unchanged.
        Assert.Equal(10_737_418_240L, pro.MaxTransferSize);  // still the seed — a limits.free.* flag never touches pro.
        Assert.Equal(10_737_418_240L, pro.MaxSingleFile);
        Assert.Equal(long.MaxValue, pro.MaxZipSize);
        Assert.Equal(30, pro.RetentionDays);
        Assert.Equal(3, pro.GraceDays);
        Assert.Equal(1000, pro.MaxDownloads);
        Assert.Equal(100, pro.MaxEmails);
        Assert.Equal(107_374_182_400L, pro.StorageQuota);
        Assert.Equal(200, pro.ActiveTransfersMax);

        var business = await provider.Resolve("business", CancellationToken.None); // and business.
        Assert.Equal(107_374_182_400L, business.MaxTransferSize);
        Assert.Equal(107_374_182_400L, business.MaxSingleFile);
        Assert.Equal(long.MaxValue, business.MaxZipSize);
        Assert.Equal(90, business.RetentionDays);
        Assert.Equal(3, business.GraceDays);
        Assert.Equal(-1, business.MaxDownloads); // -1 = unlimited (TA-4.2).
        Assert.Equal(500, business.MaxEmails);
        Assert.Equal(1_099_511_627_776L, business.StorageQuota);
        Assert.Equal(-1, business.ActiveTransfersMax);
    }

    [Fact]
    public async Task Resolve_UnknownPlan_PropagatesPlanUnknownException() // AC-053-4 → 404 PLAN_UNKNOWN at the API layer.
    {
        var provider = CreateProvider();

        var ex = await Assert.ThrowsAsync<PlanUnknownException>(() => provider.Resolve("enterprise", CancellationToken.None));

        Assert.Equal("enterprise", ex.PlanCode);
    }

    [Fact]
    public async Task Resolve_NonLimitOrUnknownFlags_AreIgnored() // EC-053-1: feature.* gates and unknown segments don't touch the record.
    {
        var provider = CreateProvider(rows: new[]
        {
            Flag("feature.ads", "true"),                 // a gate — separate read, not a limit override.
            Flag("limits.free.scheduling", "false"),     // 5th limits.* key with no LimitsRecord field → unknown segment.
            Flag("limits.unknown.someField", "1"),       // no such plan code in the row set at all.
            Flag("limits.pro.maxEmails", "999"),         // right shape, wrong plan for this request.
        });

        var free = await provider.Resolve("free", CancellationToken.None);

        Assert.Equal(20, free.MaxEmails);   // seed — limits.pro.* ignored when resolving free.
        Assert.Equal(5_368_709_120L, free.MaxTransferSize); // no matching flag for any field here.
    }

    [Fact]
    public async Task Resolve_DownStoreOnCacheMiss_PropagatesUnderlyingException() // EC-053-3: no silent fallback to hardcoded defaults.
    {
        var provider = CreateProvider(rows: Array.Empty<FeatureFlag>(), plansThrow: new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.Resolve("free", CancellationToken.None));
    }

    [Fact]
    public async Task Resolve_InfinitySentinels_BusinessIsInfinite_FreeIsNot() // T-007-01: -1 = ∞ sentinel handling — Business MAX_DOWNLOADS resolves to an infinite sentinel.
    {
        var provider = CreateProvider();

        var business = await provider.Resolve("business", CancellationToken.None);
        Assert.True(business.IsMaxDownloadsInfinite()); // -1 sentinel → IsMaxDownloadsInfinite() true (T-007-01).
        Assert.True(business.IsActiveTransfersMaxInfinite()); // -1 sentinel → IsMaxDownloadsInfinite() true (T-007-01).

        var free = await provider.Resolve("free", CancellationToken.None);
        Assert.False(free.IsMaxDownloadsInfinite()); // 100 is not -1 → false.
        Assert.False(free.IsActiveTransfersMaxInfinite()); // 20 is not -1 → false.
    }

    [Fact]
    public async Task Resolve_TtlExpiry_PicksUpChangedFlagValue() // T-007-01: TTL expiry honored — after 30 s a changed flag value is picked up (test fakes the clock).
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var planStore = new StubPlansStore();
        var flagStore = new StubFlagsStore(new[]
        {
            Flag("limits.free.maxDownloads", "100"), // initial value.
        });

        var provider = new LimitsProvider(
            new PlansCache(planStore, Ttl, () => clock.Now),
            new FlagsCache(flagStore, Ttl, () => clock.Now));

        // First resolve: within TTL, uses cached flag value.
        var first = await provider.Resolve("free", CancellationToken.None);
        Assert.Equal(100, first.MaxDownloads);

        // Change the flag value (simulating a DB update).
        flagStore.Rows = new[]
        {
            Flag("limits.free.maxDownloads", "200"), // changed value.
        };

        // Still within TTL: should still see the old cached value.
        var second = await provider.Resolve("free", CancellationToken.None);
        Assert.Equal(100, second.MaxDownloads); // cached — TTL not yet expired.

        // Advance clock past TTL.
        clock.Advance(Ttl + TimeSpan.FromSeconds(1));

        // Now the cache should refetch and see the new value.
        var third = await provider.Resolve("free", CancellationToken.None);
        Assert.Equal(200, third.MaxDownloads); // TTL expired — new value picked up.
    }

    // ── builders ──────────────────────────────────────────────────────────────

    private static LimitsProvider CreateProvider(
        IReadOnlyList<FeatureFlag>? rows = null,
        Exception? plansThrow = null)
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow); // fakes + fake clock only — no real wall-clock dependency (T-053-05).
        var planStore = new StubPlansStore { Throw = plansThrow };
        if (plansThrow is not null) planStore.ThrowForCode = "free";
        var flagStore = new StubFlagsStore(rows ?? Array.Empty<FeatureFlag>());

        return new LimitsProvider(
            new PlansCache(planStore, Ttl, () => clock.Now),
            new FlagsCache(flagStore, Ttl, () => clock.Now));
    }

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static FeatureFlag Flag(string key, string value) => new() { Key = key, Value = value };

    private sealed class StubPlansStore : IPlansStore
    {
        public Exception? Throw { get; set; }
        public string? ThrowForCode { get; set; }

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken)
        {
            if (Throw is not null && planCode == ThrowForCode) return Task.FromException<Plan>(Throw);
            var plan = new Plan
            {
                Code = planCode,
                LimitsJson = planCode switch
                {
                    "free" => FreeLimitsJson,
                    "pro" => ProLimitsJson,
                    "business" => BusinessLimitsJson,
                    _ => throw new PlanUnknownException(planCode) // real store throws the same for unknown codes (T-053-02).
                },
            };
            return Task.FromResult(plan);
        }

        // T-008-02: by-Id read (returns null or the row; never throws) — new IPlansStore member from the plan.
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(null);
    }

    private sealed class StubFlagsStore(IReadOnlyList<FeatureFlag> rows) : IFlagsStore
    {
        public IReadOnlyList<FeatureFlag> Rows { get; set; } = rows;

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(Rows);
    }
}
