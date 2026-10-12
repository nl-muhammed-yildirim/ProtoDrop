using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-052-05 (AC-052-2, third Gherkin block): fresh SQL 2022 via Testcontainers (TA-14.3), both migrations run
/// (InitialCreate + SeedPlansAndFeatureFlags), then the seeds are asserted verbatim — identical fixtures as local
/// Docker SQL (TA-14.3). The frozen Part-2 constants below encode EC-052-3: if a value differs from
/// Open-Decisions-and-Constants.md Part 2, fix the seed, not this test.
/// </summary>
public class SeedDataIntegrationTest : IAsyncLifetime
{
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await container.StartAsync();

        // master is the SQL Server's default DB — migrations create the schema + seeds there; nothing else to drop.
        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(container.GetConnectionString())
            .Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // ---- AC-052-2: exactly 3 rows, fixed GUIDs, Part-2 LimitsJson/FeaturesJson ----

    [Fact]
    public async Task Plan_HasExactlyThreeSeedRowsWithFixedGuidsAndPartTwoValues()
    {
        var plans = await _db.Set<Plan>().ToListAsync();

        Assert.Equal(3, plans.Count);
        Assert.Subset(plans.Select(p => (p.Id, p.Code)).Distinct().ToHashSet(), ExpectedPlans.Select(p => (p.Id, p.Code)).ToHashSet());
        foreach (var expected in ExpectedPlans)
            Assert.Contains(plans, p => p.Id == expected.Id && p.Code == expected.Code
                && p.Name == expected.Name && p.SortOrder == expected.SortOrder
                && p.LimitsJson == expected.LimitsJson && p.FeaturesJson == expected.FeaturesJson);
    }

    // ---- AC-052-2: all seeded keys present — 12 feature gates (false per D-14) + 42 limit keys ----

    [Fact]
    public async Task FeatureFlag_HasAllSeededKeys()
    {
        var flags = await _db.Set<FeatureFlag>().ToListAsync();

        Assert.Equal(54, flags.Count); // 12 feature.* gates + 3 x 14 limits.<plan>.* keys (FR-052-4)
        Assert.Subset(flags.Select(f => f.Key).ToHashSet(), ExpectedKeys().ToHashSet());
    }

    private static readonly Plan[] ExpectedPlans =
    {
        new()
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000001"),
            Code = "free", Name = "Free", SortOrder = 1,
            LimitsJson = "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}",
            FeaturesJson = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}"
        },
        new()
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000002"),
            Code = "pro", Name = "Pro", SortOrder = 2,
            LimitsJson = "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}",
            FeaturesJson = "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":false}"
        },
        new()
        {
            Id = Guid.Parse("11111111-0000-4000-8000-000000000003"),
            Code = "business", Name = "Business", SortOrder = 3,
            LimitsJson = "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}",
            FeaturesJson = "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":true}"
        }
    };

    private static IEnumerable<string> ExpectedKeys() =>
        FeatureGateKeys.Concat(LimitSuffixes.SelectMany(suffix => new[] { "free", "pro", "business" }.Select(plan => $"limits.{plan}.{suffix}")));

    private static readonly string[] FeatureGateKeys =
    [
        "feature.ads", "feature.albums", "feature.analytics", "feature.branding", "feature.collect",
        "feature.dataExport", "feature.emailSettings", "feature.resend", "feature.scheduling",
        "feature.search", "feature.sign", "feature.ssoScim"
    ];

    private static readonly string[] LimitSuffixes =
    [
        "activeTransfersMax", "ads", "analytics", "branding", "graceDays", "maxDownloads", "maxEmails",
        "maxSingleFile", "maxTransferSize", "maxZipSize", "retentionDays", "scheduling", "ssoScim", "storageQuota"
    ];
}
