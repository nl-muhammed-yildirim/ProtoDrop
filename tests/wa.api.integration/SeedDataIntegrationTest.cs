using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-004 exit check: seed rows verifiable. Runs the full `WaDbContext` migration
/// graph into a disposable MSSQL container and asserts the frozen baseline —
/// 3 `Plan` rows (Part-2 constants) + 12 feature-gate flags (default false,
/// D-14 ads off) + 3×14 plan-limit overrides.
/// </summary>
public class SeedDataIntegrationTest
{
    static MsSqlBuilder Builder() => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithDatabase("wa_test")
        .WithPassword("YourStrong!Passw0rd");

    [Fact]
    public async Task Plans_AreSeeded()
    {
        await using var sql = Builder().Build();
        await sql.StartAsync();
        await using (var ctx = New(sql))
            await ctx.Database.MigrateAsync();
        using var verify = New(sql);

        var plans = await verify.Plans.OrderBy(p => p.SortOrder).ToListAsync();
        Assert.Equal(3, plans.Count);
        var byCode = plans.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("free", byCode.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("pro", byCode.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("business", byCode.Keys, StringComparer.OrdinalIgnoreCase);

        // Fixed GUIDs so FKs stay portable across environments (TA-3.7).
        Assert.Equal(new Guid("11111111-0000-4000-8000-000000000001"), byCode["free"].Id);
        Assert.Equal(new Guid("11111111-0000-4000-8000-000000000002"), byCode["pro"].Id);
        Assert.Equal(new Guid("11111111-0000-4000-8000-000000000003"), byCode["business"].Id);

        // Part-2 hard limits embedded per plan (never literals; TA-3.4).
        Assert.Contains("\"maxTransferSize\":5368709120", byCode["free"].LimitsJson);
        Assert.Contains("\"maxDownloads\":100", byCode["free"].LimitsJson);
        // D-07 stand-in 10 GB (ESCALATIONS.md E-004) — Part 2 says "TBD (D-07)".
        Assert.Contains("\"maxTransferSize\":10737418240", byCode["pro"].LimitsJson);
        Assert.Contains("\"maxDownloads\":-1", byCode["business"].LimitsJson);
        Assert.Contains("\"retentionDays\":90", byCode["business"].LimitsJson);

        // Feature gates (F-BIL-001 shape); only business enables sso_scim.
        Assert.Contains("\"sso_scim\":true", byCode["business"].FeaturesJson);
        Assert.Contains("\"sso_scim\":false", byCode["free"].FeaturesJson);
    }

    [Fact]
    public async Task FeatureFlag_GateKeys_AreSeeded()
    {
        await using var sql = Builder().Build();
        await sql.StartAsync();
        await using (var ctx = New(sql))
            await ctx.Database.MigrateAsync();
        using var verify = New(sql);

        var gates = (await verify.FeatureFlags.ToListAsync())
            .Where(f => f.Key.StartsWith("feature.", StringComparison.Ordinal))
            .ToList();

        // TA-13.2 canonical gate keys, all default false (D-14: ads off at launch).
        Assert.Equal(12, gates.Count);
        var expected = new[]
        {
            "feature.ads", "feature.scheduling", "feature.analytics", "feature.resend",
            "feature.collect", "feature.sign", "feature.albums", "feature.search",
            "feature.dataExport", "feature.emailSettings", "feature.branding", "feature.ssoScim",
        };
        foreach (var key in expected)
            Assert.Contains(key, gates.Select(g => g.Key).ToArray());
    }

    [Fact]
    public async Task FeatureFlag_PlanLimitKeys_AreSeeded()
    {
        await using var sql = Builder().Build();
        await sql.StartAsync();
        await using (var ctx = New(sql))
            await ctx.Database.MigrateAsync();
        using var verify = New(sql);

        var limits = (await verify.FeatureFlags.ToListAsync())
            .Where(f => f.Key.StartsWith("limits.", StringComparison.Ordinal))
            .ToList();

        // TA-13.2: one flag per Part-2 constant per plan (14 constants × 3 plans = 42).
        Assert.Equal(42, limits.Count);
        Assert.Equal("5368709120", Value(limits, "limits.free.maxTransferSize"));
        Assert.Equal("10737418240", Value(limits, "limits.pro.maxTransferSize"));
        Assert.Equal("107374182400", Value(limits, "limits.business.maxTransferSize"));
        Assert.Equal("7", Value(limits, "limits.free.retentionDays"));
        Assert.Equal("30", Value(limits, "limits.pro.retentionDays"));
        Assert.Equal("90", Value(limits, "limits.business.retentionDays"));
        Assert.Equal("100", Value(limits, "limits.free.maxDownloads"));
        Assert.Equal("1000", Value(limits, "limits.pro.maxDownloads"));
        Assert.Equal("-1", Value(limits, "limits.business.maxDownloads"));
        Assert.Equal("true", Value(limits, "limits.business.ssoScim"));
        Assert.Equal("false", Value(limits, "limits.free.ssoScim"));
    }

    static WaDbContext New(MsSqlContainer sql)
        => new(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(sql.GetConnectionString())
            .Options);

    static string Value(List<FeatureFlag> all, string key)
    {
        var f = all.FirstOrDefault(x => x.Key == key);
        if (f is null) throw new Xunit.Sdk.XunitException($"missing key {key}");
        return f.Value;
    }
}
