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
    private const string FreeLimits = "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}";
    private const string FreeFeatures = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}";

    private MsSqlContainer _sql = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
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

        await using var readDb = new WaDbContext(_options);
        var provider = new LimitsProvider(
            new PlansCache(new PlansStore(readDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
            new FlagsCache(new FlagsStore(readDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow));

        var limits = await provider.Resolve("proplus", CancellationToken.None);

        Assert.Equal(45, limits.RetentionDays);
        Assert.Equal(150, limits.MaxEmails);
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

    private static Plan NewPlan(string code, int sortOrder, string limitsJson) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code == "proplus" ? "Pro Plus" : "Pro Plus 2",
        SortOrder = sortOrder,
        LimitsJson = limitsJson,
        FeaturesJson = FreeFeatures
    };
}
