using wa.application.Limits;
using wa.domain;

namespace wa.application.unit;

/// <summary>
/// T-005 (part B) exit checks. Fakes the Plan/FeatureFlag data boundary
/// (<see cref="IPlansStore"/> / <see cref="IFlagsStore"/> — no Testcontainers)
/// and drives the TA-3.4 30 s cache TTL with an injectable clock:
///   (a) resolution per plan — free/pro/business each resolve their
///       Open-Decisions-and-Constants.md Part 2 values from <c>Plan.LimitsJson</c>;
///   (b) flag override wins — a <c>limits.{plan}.*</c> FeatureFlag changes ONLY
///       its one field; all other values stay unchanged;
///   (c) TTL honored — a fresh entry is not refetched, one older than 30 s is.
/// </summary>
public class LimitsProviderTests
{
    // ── Part-2 limit constants (docs/Open-Decisions-and-Constants.md) ─────────
    const long FiveGb = 5368709120;
    const long FourGb = 4294967296;
    const long TenGb = 10737418240;        // Pro MAX_TRANSFER_SIZE — D-07 stand-in (E-004)
    const long HundredGb = 107374182400;
    const long OneTb = 1099511627776;
    const long LongMax = 9223372036854775807;

    // Seeded <c>Plan.LimitsJson</c> (wa.infrastructure PlanConfiguration, Part 2).
    const string LimitsFree =
        "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}";
    const string LimitsPro =
        "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}";
    const string LimitsBusiness =
        "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}";

    // ── (a) resolution per plan: Part-2 values resolve from LimitsJson ────────

    [Theory]
    [InlineData("free", FiveGb, FiveGb, FourGb, 7, 3, 100, 20, FiveGb, 20)]
    [InlineData("pro", TenGb, TenGb, LongMax, 30, 3, 1000, 100, HundredGb, 200)]
    [InlineData("business", HundredGb, HundredGb, LongMax, 90, 3, -1, 500, OneTb, -1)]
    public async Task Resolve_Plan_ResolvesPart2Values_FromLimitsJson(
        string planCode, long maxTransferSize, long maxSingleFile, long maxZipSize,
        int retentionDays, int graceDays, int maxDownloads, int maxEmails,
        long storageQuota, int activeTransfersMax)
    {
        var plans = new FakePlansStore { Rows = Plans };
        var flags = new FakeFlagsStore();
        var provider = new LimitsProvider(
            new PlansCache(plans, new FakeClock().Source),
            new FlagsCache(flags, new FakeClock().Source));

        var r = await provider.ResolveAsync(planCode);

        Assert.Equal(maxTransferSize, r.MaxTransferSize);
        Assert.Equal(maxSingleFile, r.MaxSingleFile);
        Assert.Equal(maxZipSize, r.MaxZipSize);
        Assert.Equal(retentionDays, r.RetentionDays);
        Assert.Equal(graceDays, r.GraceDays);
        Assert.Equal(maxDownloads, r.MaxDownloads);
        Assert.Equal(maxEmails, r.MaxEmails);
        Assert.Equal(storageQuota, r.StorageQuota);
        Assert.Equal(activeTransfersMax, r.ActiveTransfersMax);
    }

    [Fact]
    public async Task Resolve_UnknownPlanCode_Throws()
    {
        var provider = new LimitsProvider(
            new PlansCache(new FakePlansStore { Rows = Plans }, new FakeClock().Source),
            new FlagsCache(new FakeFlagsStore(), new FakeClock().Source));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => provider.ResolveAsync("enterprise"));
    }

    // ── (b) flag override wins, everything else unchanged ─────────────────────

    [Fact]
    public async Task Resolve_FlagOverride_ChangesOnlyItsOneField()
    {
        var plans = new FakePlansStore { Rows = Plans };
        var flags = new FakeFlagsStore
        {
            Rows =
            [
                // The override (US-007-04 style edit): free MAX_TRANSFER_SIZE 5 GB → 10 GB.
                new FlagRow("limits.free.maxTransferSize", "10737418240"),
                // Same plan, a second limit key — stays at baseline, must NOT change.
                new FlagRow("limits.free.retentionDays", "7"),
                // Other plan's key — must not leak into "free".
                new FlagRow("limits.pro.retentionDays", "30"),
                // Feature gate — ignored by the limits provider.
                new FlagRow("feature.ads", "false"),
                // Unknown limit segment — ignored.
                new FlagRow("limits.free.notALimit", "1"),
            ]
        };
        var provider = new LimitsProvider(
            new PlansCache(plans, new FakeClock().Source),
            new FlagsCache(flags, new FakeClock().Source));

        var r = await provider.ResolveAsync("free");

        Assert.Equal(TenGb, r.MaxTransferSize); // ← override wins
        // every other resolved value is still the Part-2 free baseline
        Assert.Equal(FiveGb, r.MaxSingleFile);
        Assert.Equal(FourGb, r.MaxZipSize);
        Assert.Equal(7, r.RetentionDays);
        Assert.Equal(3, r.GraceDays);
        Assert.Equal(100, r.MaxDownloads);
        Assert.Equal(20, r.MaxEmails);
        Assert.Equal(FiveGb, r.StorageQuota);
        Assert.Equal(20, r.ActiveTransfersMax);

        // the "pro" plan is untouched by the free-plan override list
        var pro = await provider.ResolveAsync("pro");
        Assert.Equal(TenGb, pro.MaxTransferSize);
        Assert.Equal(30, pro.RetentionDays);
    }

    // ── (c) TTL honored: >30 s refetches, fresh does not ─────────────────────

    [Fact]
    public async Task PlansCache_Ttl_FreshNotRefetched_StaleRefetched()
    {
        var store = new FakePlansStore { Rows = Plans };
        var clock = new FakeClock();
        var cache = new PlansCache(store, clock.Source);

        var first = await cache.GetPlansAsync();
        Assert.Equal(1, store.ReadCount);

        // source data changes (admin edits a plan row) …
        store.Rows = [free(TenGb), pro(), business()];
        clock.Advance(TimeSpan.FromSeconds(10)); // … but the entry is still fresh
        var fresh = await cache.GetPlansAsync();
        Assert.Same(first, fresh);
        Assert.Equal(1, store.ReadCount);

        clock.Advance(TimeSpan.FromSeconds(20)); // 30 s total — at the TTL boundary
        var stale = await cache.GetPlansAsync();
        Assert.Equal(2, store.ReadCount); // refetched
        Assert.Equal(TenGb, FreeMaxTransfer(stale)); // … and the new value is live
    }

    [Fact]
    public async Task FlagsCache_Ttl_FreshNotRefetched_StaleRefetched()
    {
        var store = new FakeFlagsStore { Rows = [new FlagRow("limits.free.retentionDays", "7")] };
        var clock = new FakeClock();
        var cache = new FlagsCache(store, clock.Source);

        var first = await cache.GetFlagsAsync();
        Assert.Equal(1, store.ReadCount);

        store.Rows = [new FlagRow("limits.free.retentionDays", "14")]; // admin edit
        clock.Advance(TimeSpan.FromSeconds(10)); // fresh → cached value stays
        var fresh = await cache.GetFlagsAsync();
        Assert.Same(first, fresh);
        Assert.Equal(1, store.ReadCount);

        clock.Advance(TimeSpan.FromSeconds(20)); // 30 s total — at the TTL boundary
        var stale = await cache.GetFlagsAsync();
        Assert.Equal(2, store.ReadCount); // refetched
        Assert.Equal("14", stale[0].Value); // … and the edit is live
    }

    [Fact]
    public void Ttl_Is30Seconds_PerPart2TechnicalConstant()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), PlansCache.Ttl);
        Assert.Equal(TimeSpan.FromSeconds(30), FlagsCache.Ttl);
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    static readonly IReadOnlyList<PlanRow> Plans = new[]
    {
        PlanRow("free", "Free", LimitsFree, 1),
        PlanRow("pro", "Pro", LimitsPro, 2),
        PlanRow("business", "Business", LimitsBusiness, 3),
    };

    static PlanRow PlanRow(string code, string name, string limitsJson, int sortOrder) =>
        new(PlanGuid(sortOrder), code, name, limitsJson, "{}", sortOrder);

    static Guid PlanGuid(int n) => new($"11111111-0000-4000-8000-{n:D12}");

    static PlanRow free(long maxTransferSize) =>
        PlanRow("free", "Free",
            $"{{\"maxTransferSize\":{maxTransferSize},\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}}", 1);

    static PlanRow pro() => PlanRow("pro", "Pro", LimitsPro, 2);

    static PlanRow business() => PlanRow("business", "Business", LimitsBusiness, 3);

    static long FreeMaxTransfer(IReadOnlyList<PlanRow> plans)
    {
        var json = plans.First(p => p.Code == "free").LimitsJson;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("maxTransferSize").GetInt64();
    }

    sealed class FakePlansStore : IPlansStore
    {
        public IReadOnlyList<PlanRow> Rows { get; set; } = [];
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<PlanRow>> GetPlansAsync(CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<PlanRow>>(Rows);
        }
    }

    sealed class FakeFlagsStore : IFlagsStore
    {
        public IReadOnlyList<FlagRow> Rows { get; set; } = [];
        public int ReadCount { get; private set; }

        public Task<IReadOnlyList<FlagRow>> GetFlagsAsync(CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<FlagRow>>(Rows);
        }
    }

    sealed class FakeClock
    {
        DateTimeOffset _now = DateTimeOffset.UtcNow;
        public Func<DateTimeOffset> Source => () => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
