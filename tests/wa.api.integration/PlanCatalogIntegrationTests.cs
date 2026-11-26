using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-031-01 (US-018-01): plans are data. Fresh SQL 2022 via Testcontainers (TA-14.3), migrations applied (seeds included).
/// Covers EC-018-2 on the real seeds, "new plan = new row" resolving through ILimitsProvider with no code change,
/// and EC-018-4 (FK_AppUser_Plan blocks deleting a plan that has users).
/// </summary>
public class PlanCatalogIntegrationTests : IAsyncLifetime
{
    private const string FreeLimits = "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20,\"maxLogoSize\":1048576}";
    private const string FreeFeatures = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}";

    private const string FreePrice = "{\"currency\":\"USD\",\"monthly\":{\"amount\":0,\"stripePriceId\":null},\"annual\":{\"amount\":0,\"stripePriceId\":null},\"perSeatMonthly\":null}";

    private MsSqlContainer _sql = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(_options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    [Fact]
    public async Task SeededPlans_AllPassPlanDefinitionValidation()
    {
        var plans = await _db.Set<Plan>().ToListAsync();

        Assert.Equal(3, plans.Count);
        foreach (var plan in plans)
            Assert.True(PlanDefinition.Validate(plan).Count == 0, $"Seed plan '{plan.Code}': " + string.Join("; ", PlanDefinition.Validate(plan)));
    }

    [Fact]
    public async Task NewPlanRow_WithValidJson_ResolvesThroughLimitsProvider_NoCodeChange()
    {
        var plan = NewPlan("proplus", 4, FreeLimits.Replace("\"retentionDays\":7", "\"retentionDays\":45").Replace("\"maxEmails\":20", "\"maxEmails\":150"));
        Assert.Empty(PlanDefinition.Validate(plan));
        _db.Set<Plan>().Add(plan);
        await _db.SaveChangesAsync();

        var readDb = new AppDbContextFactory(_options);
        var provider = new LimitsProvider(
            new PlansCache(new PlansStore(readDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
            new FlagsCache(new FlagsStore(readDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow));

        var limits = await provider.Resolve("proplus", CancellationToken.None);

        Assert.Equal(45, limits.RetentionDays);
        Assert.Equal(150, limits.MaxEmails);
    }

    [Fact]
    public async Task SeededProFlag_Edited_ResolvesNewValue_OnlyAfterTtl() // AC-018-2 / US-018-02 AC blocks 1-2 on real SQL.
    {
        var proId = Guid.Parse("11111111-0000-4000-8000-000000000002");
        var pro = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == proId);
        var expected = JsonSerializer.Deserialize<LimitsRecord>(pro.LimitsJson)!.MaxTransferSize;

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromSeconds(30);
        var readDb = new AppDbContextFactory(_options);
        var provider = new LimitsProvider(
            new PlansCache(new PlansStore(readDb), ttl, () => now),
            new FlagsCache(new FlagsStore(readDb), ttl, () => now));

        var first = await provider.Resolve(pro.Code, CancellationToken.None);
        Assert.Equal(expected, first.MaxTransferSize);

        await using (var writeDb = new WaDbContext(_options))
        {
            var flag = await writeDb.Set<FeatureFlag>().SingleAsync(f => f.Key == "limits." + pro.Code + ".maxTransferSize");
            flag.Value = "32212254720"; // 30 GB — a test value, not a constant change.
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await writeDb.SaveChangesAsync();
        }

        now += TimeSpan.FromSeconds(29);
        var within = await provider.Resolve(pro.Code, CancellationToken.None);
        Assert.Equal(expected, within.MaxTransferSize);

        now += TimeSpan.FromSeconds(2);
        var after = await provider.Resolve(pro.Code, CancellationToken.None);
        Assert.Equal(32_212_254_720L, after.MaxTransferSize);
    }

    [Fact]
    public async Task SeededPaidPlans_PricesReadFromRow_NoStripeIdInvented() // AC-018-4 price source, US-018-03 AC block 1.
    {
        var pro = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == Guid.Parse("11111111-0000-4000-8000-000000000002"));
        var business = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == Guid.Parse("11111111-0000-4000-8000-000000000003"));

        var proPrice = PlanPrice.Read(pro);
        var businessPrice = PlanPrice.Read(business);

        Assert.Equal(900, proPrice.For(BillingInterval.Monthly).Amount);
        Assert.Equal(9000, proPrice.For(BillingInterval.Annual).Amount);
        Assert.NotNull(businessPrice.PerSeatMonthly);
        foreach (var price in new[] { proPrice, businessPrice })
            foreach (var interval in Enum.GetValues<BillingInterval>())
                Assert.False(price.For(interval).IsWiredToStripe()); // operator-set (D-12), never invented.
    }

    [Fact]
    public async Task PriceChange_IsRowUpdate_VisibleThroughPlansCache() // alternative flow: price change = data change, no deploy.
    {
        var proId = Guid.Parse("11111111-0000-4000-8000-000000000002");
        await using (var writeDb = new WaDbContext(_options))
        {
            var pro = await writeDb.Set<Plan>().SingleAsync(p => p.Id == proId);
            pro.PriceJson = pro.PriceJson.Replace("\"amount\":900,", "\"amount\":1200,");
            Assert.Empty(PlanDefinition.Validate(pro)); // validate-then-write (EC-018-2)
            await writeDb.SaveChangesAsync();
        }

        var readDb = new AppDbContextFactory(_options);
        var cache = new PlansCache(new PlansStore(readDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow);
        var plan = await cache.GetAsync("pro", CancellationToken.None);

        Assert.Equal(1200, PlanPrice.Read(plan).For(BillingInterval.Monthly).Amount);
    }

    [Fact]
    public async Task DeletingPlan_WithUsers_IsBlockedByFk()
    {
        var proId = Guid.Parse("11111111-0000-4000-8000-000000000002");
        _db.Set<AppUser>().Add(new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "plan-fk@example.com",
            DisplayName = "Plan FK",
            PlanId = proId,
            PasswordHash = "x",
            EmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Fresh context: the user is not tracked, so EF does not intercept the delete — the DB constraint must.
        await using var deleteDb = new WaDbContext(_options);
        var pro = await deleteDb.Set<Plan>().SingleAsync(p => p.Id == proId);
        deleteDb.Set<Plan>().Remove(pro);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => deleteDb.SaveChangesAsync());

        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException) messages.Add(e.Message);
        Assert.Contains("FK_AppUser_Plan", string.Join("\n", messages));

        await using var fresh = new WaDbContext(_options);
        Assert.True(await fresh.Set<Plan>().AnyAsync(p => p.Id == proId));
    }

    [Fact]
    public async Task DeletingPlan_WithoutUsers_Succeeds()
    {
        var plan = NewPlan("proplus2", 5, FreeLimits);
        _db.Set<Plan>().Add(plan);
        await _db.SaveChangesAsync();

        _db.Set<Plan>().Remove(plan);
        await _db.SaveChangesAsync();

        await using var fresh = new WaDbContext(_options);
        Assert.False(await fresh.Set<Plan>().AnyAsync(p => p.Code == "proplus2"));
    }

    private static readonly Guid FreeId = Guid.Parse("11111111-0000-4000-8000-000000000001");
    private static readonly Guid ProId = Guid.Parse("11111111-0000-4000-8000-000000000002");
    private static readonly Guid BusinessId = Guid.Parse("11111111-0000-4000-8000-000000000003");

    private FeaturesProvider NewFeaturesProvider() => new(
        new PlansCache(new PlansStore(new AppDbContextFactory(_options)), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
        new FlagsCache(new FlagsStore(new AppDbContextFactory(_options)), TimeSpan.Zero, () => DateTimeOffset.UtcNow));

    private async Task SetGate(string key, string value)
    {
        await using var writeDb = new WaDbContext(_options);
        var flag = await writeDb.Set<FeatureFlag>().SingleAsync(f => f.Key == key);
        flag.Value = value;
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await writeDb.SaveChangesAsync();
    }

    private static void AssertSame(PlanFeatures expected, PlanFeatures actual)
    {
        Assert.Equal(expected.Ads, actual.Ads);
        Assert.Equal(expected.Scheduling, actual.Scheduling);
        Assert.Equal(expected.Analytics, actual.Analytics);
        Assert.Equal(expected.Branding, actual.Branding);
        Assert.Equal(expected.SsoScim, actual.SsoScim);
    }

    [Fact]
    public async Task SeededGlobalGates_ExistForEveryPlanFeature() // guards key derivation against the TA-13.2 seed.
    {
        var keys = await _db.Set<FeatureFlag>().AsNoTracking().Select(f => f.Key).ToListAsync();

        foreach (var gate in FeaturesProvider.GlobalGateKeys)
            Assert.Contains(gate, keys);
    }

    [Fact]
    public async Task SeededGates_Off_EveryPlanResolvesAllFeaturesOff()
    {
        var provider = NewFeaturesProvider();

        foreach (var id in new[] { FreeId, ProId, BusinessId })
        {
            var plan = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == id);
            AssertSame(new PlanFeatures(), await provider.Resolve(plan.Code, CancellationToken.None));
        }
    }

    [Fact]
    public async Task GatesOn_ProRowDrives_FreeOnlyAds_RowAndGateEditsApplyWithoutDeploy()
    {
        foreach (var key in FeaturesProvider.GlobalGateKeys) await SetGate(key, "true");
        var free = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == FreeId);
        var pro = await _db.Set<Plan>().AsNoTracking().SingleAsync(p => p.Id == ProId);

        var provider = NewFeaturesProvider();

        AssertSame(JsonSerializer.Deserialize<PlanFeatures>(pro.FeaturesJson)!, await provider.Resolve(pro.Code, CancellationToken.None));
        AssertSame(new PlanFeatures { Ads = true }, await provider.Resolve(free.Code, CancellationToken.None)); // Free seed carries ads:true (AddFreeAdsEnabled)

        await SetGate(IFeaturesProvider.GlobalGatePrefix + "scheduling", "false");
        Assert.False((await provider.Resolve(pro.Code, CancellationToken.None)).Scheduling);

        await SetGate(IFeaturesProvider.GlobalGatePrefix + "scheduling", "true");
        await using (var writeDb = new WaDbContext(_options))
        {
            var row = await writeDb.Set<Plan>().SingleAsync(p => p.Id == ProId);
            row.FeaturesJson = row.FeaturesJson.Replace("\"analytics\":true", "\"analytics\":false");
            Assert.Empty(PlanDefinition.Validate(row)); // validate-then-write (EC-018-2)
            await writeDb.SaveChangesAsync();
        }
        var after = await provider.Resolve(pro.Code, CancellationToken.None);
        Assert.False(after.Analytics);
        Assert.True(after.Scheduling);
    }

    private static Plan NewPlan(string code, int sortOrder, string limitsJson) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code == "proplus" ? "Pro Plus" : "Pro Plus 2",
        SortOrder = sortOrder,
        LimitsJson = limitsJson,
        FeaturesJson = FreeFeatures,
        PriceJson = FreePrice
    };
}
