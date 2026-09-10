using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.domain.unit;

/// <summary>
/// T-004c exit checks (unit level). Seeds are read straight off the EF model
/// (HasData in wa.infrastructure ModelConfigurations), so no Docker is needed.
///   (a) each Plan's LimitsJson parses to the Open-Decisions-and-Constants.md
///       Part 2 values exactly (no invented numbers; Pro maxTransferSize is the
///       D-07 stand-in 10 GB per ESCALATIONS.md E-004), and
///   (b) the FeatureFlag seed covers every TA-13.2 key: 12 feature gates (all
///       false) + 14 limits.{plan}.* overrides per 3 plans.
/// </summary>
public class SeedValuesUnitTest
{
    // ── Part-2 constants (docs/Open-Decisions-and-Constants.md) ──────────────
    const long FreeTransfer = 5368709120;       // 5 GB
    const long FreeStorage = 5368709120;        // 5 GB
    const long FreeZip = 4294967296;            // 4 GB
    const long ProTransfer = 10737418240;       // 10 GB — D-07 stand-in (E-004)
    const long ProStorage = 107374182400;       // 100 GB
    const long MaxZip = 9223372036854775807;    // long.MaxValue
    const long BizTransfer = 107374182400;      // 100 GB
    const long BizStorage = 1099511627776;      // 1 TB

    // ── TA-13.2 canonical flag-key seed list ─────────────────────────────────
    static readonly string[] GateKeys =
    [
        "feature.ads", "feature.scheduling", "feature.analytics", "feature.resend",
        "feature.collect", "feature.sign", "feature.albums", "feature.search",
        "feature.dataExport", "feature.emailSettings", "feature.branding", "feature.ssoScim",
    ];
    static readonly string[] LimitFields =
    [
        "maxTransferSize", "maxSingleFile", "maxZipSize", "retentionDays",
        "graceDays", "maxDownloads", "maxEmails", "storageQuota", "activeTransfersMax",
        "scheduling", "branding", "analytics", "ads", "ssoScim",
    ];
    static readonly string[] PlanCodes = ["free", "pro", "business"];

    static readonly Dictionary<Guid, string> PlanGuids = new()
    {
        [new Guid("11111111-0000-4000-8000-000000000001")] = "free",
        [new Guid("11111111-0000-4000-8000-000000000002")] = "pro",
        [new Guid("11111111-0000-4000-8000-000000000003")] = "business",
    };

    // Seed rows live on the design-time model (HasData) — exactly what the
    // migration ships. (The runtime model is read-optimized and strips them.)
    static IReadOnlyModel DesignModel()
    {
        var options = new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlite("DataSource=:memory:").Options;
        using var ctx = new WaDbContext(options);
        return ctx.GetService<IDesignTimeModel>().Model;
    }

    static (Dictionary<string, string> Plans, Dictionary<string, string> Flags) Seeds()
    {
        var model = DesignModel();

        var plans = new Dictionary<string, string>();
        foreach (var row in model.FindEntityType(typeof(Plan))!.GetSeedData())
            plans[(string)row["Code"]!] = (string)row["LimitsJson"]!;

        var flags = new Dictionary<string, string>();
        foreach (var row in model.FindEntityType(typeof(FeatureFlag))!.GetSeedData())
            flags[(string)row["Key"]!] = (string)row["Value"]!;

        return (plans, flags);
    }

    static void Limit(JsonElement e, string field, long expected) =>
        Assert.Equal(expected, e.GetProperty(field).GetInt64());

    // ── (a) Plan.LimitsJson ───────────────────────────────────────────────────
    [Fact]
    public void Plans_are_seeded_free_pro_business_with_frozen_guids()
    {
        var model = DesignModel();
        var rows = model.FindEntityType(typeof(Plan))!.GetSeedData().ToList();

        Assert.Equal(3, rows.Count);
        foreach (var (guid, code) in PlanGuids)
        {
            var row = rows.Single(r => (Guid)r["Id"]! == guid);
            Assert.Equal(code, (string)row["Code"]!);
        }
    }

    [Fact]
    public void Free_LimitsJson_parses_to_Part2_values()
    {
        var (plans, _) = Seeds();
        var lim = JsonDocument.Parse(plans["free"]).RootElement;

        Limit(lim, "maxTransferSize", FreeTransfer);
        Limit(lim, "maxSingleFile", FreeTransfer);
        Limit(lim, "maxZipSize", FreeZip);
        Limit(lim, "retentionDays", 7);
        Limit(lim, "graceDays", 3);
        Limit(lim, "maxDownloads", 100);
        Limit(lim, "maxEmails", 20);
        Limit(lim, "storageQuota", FreeStorage);
        Limit(lim, "activeTransfersMax", 20);
    }

    [Fact]
    public void Pro_LimitsJson_parses_to_Part2_values_with_D07_standin()
    {
        var (plans, _) = Seeds();
        var lim = JsonDocument.Parse(plans["pro"]).RootElement;

        // D-07 known hole (ESCALATIONS.md E-004): Part 2 says "TBD (D-07)" —
        // 10 GB stand-in, not an invented 20/50 GB.
        Limit(lim, "maxTransferSize", ProTransfer);
        Limit(lim, "maxSingleFile", ProTransfer);
        Limit(lim, "maxZipSize", MaxZip);
        Limit(lim, "retentionDays", 30);
        Limit(lim, "graceDays", 3);
        Limit(lim, "maxDownloads", 1000);
        Limit(lim, "maxEmails", 100);
        Limit(lim, "storageQuota", ProStorage);
        Limit(lim, "activeTransfersMax", 200);
    }

    [Fact]
    public void Business_LimitsJson_parses_to_Part2_values()
    {
        var (plans, _) = Seeds();
        var lim = JsonDocument.Parse(plans["business"]).RootElement;

        Limit(lim, "maxTransferSize", BizTransfer);
        Limit(lim, "maxSingleFile", BizTransfer);
        Limit(lim, "maxZipSize", MaxZip);
        Limit(lim, "retentionDays", 90);
        Limit(lim, "graceDays", 3);
        Limit(lim, "maxDownloads", -1);
        Limit(lim, "maxEmails", 500);
        Limit(lim, "storageQuota", BizStorage);
        Limit(lim, "activeTransfersMax", -1);
    }

    [Theory]
    [InlineData("free", false, false, false, false, false)]
    [InlineData("pro", true, true, true, false, false)]
    [InlineData("business", true, true, true, false, true)]
    public void FeaturesJson_parses_to_Part2_feature_gates(
        string code, bool scheduling, bool analytics, bool branding, bool ads, bool ssoScim)
    {
        var row = DesignModel().FindEntityType(typeof(Plan))!
            .GetSeedData().Single(r => (string)r["Code"]! == code);
        var f = JsonDocument.Parse((string)row["FeaturesJson"]!).RootElement;

        Assert.Equal(scheduling, f.GetProperty("scheduling").GetBoolean());
        Assert.Equal(analytics, f.GetProperty("analytics").GetBoolean());
        Assert.Equal(branding, f.GetProperty("branding").GetBoolean());
        Assert.Equal(ads, f.GetProperty("ads").GetBoolean());
        Assert.Equal(ssoScim, f.GetProperty("sso_scim").GetBoolean());
    }

    // ── (b) FeatureFlag seed covers the full TA-13.2 key list ────────────────
    [Fact]
    public void Flag_seed_covers_every_TA13_2_key_exactly()
    {
        var (_, flags) = Seeds();

        var expected =
            GateKeys.Select(k => k)
            .Concat(PlanCodes.SelectMany(p => LimitFields.Select(f => $"limits.{p}.{f}")))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expected.Count, flags.Count);
        foreach (var key in expected)
            Assert.True(flags.ContainsKey(key), $"missing TA-13.2 key: {key}");
    }

    [Fact]
    public void Gate_flags_are_all_false()
    {
        var (_, flags) = Seeds();
        foreach (var key in GateKeys)
            Assert.Equal("false", flags[key]);
    }

    [Fact]
    public void Limit_flags_match_Part2_values()
    {
        var (_, flags) = Seeds();

        // free
        Assert.Equal("5368709120", flags["limits.free.maxTransferSize"]);
        Assert.Equal("5368709120", flags["limits.free.maxSingleFile"]);
        Assert.Equal("4294967296", flags["limits.free.maxZipSize"]);
        Assert.Equal("7", flags["limits.free.retentionDays"]);
        Assert.Equal("3", flags["limits.free.graceDays"]);
        Assert.Equal("100", flags["limits.free.maxDownloads"]);
        Assert.Equal("20", flags["limits.free.maxEmails"]);
        Assert.Equal("5368709120", flags["limits.free.storageQuota"]);
        Assert.Equal("20", flags["limits.free.activeTransfersMax"]);
        Assert.Equal("false", flags["limits.free.scheduling"]);
        Assert.Equal("false", flags["limits.free.branding"]);
        Assert.Equal("false", flags["limits.free.analytics"]);
        Assert.Equal("false", flags["limits.free.ads"]);
        Assert.Equal("false", flags["limits.free.ssoScim"]);

        // pro — D-07 stand-in 10 GB (E-004); storage 100 GB per Part 2.
        Assert.Equal("10737418240", flags["limits.pro.maxTransferSize"]);
        Assert.Equal("10737418240", flags["limits.pro.maxSingleFile"]);
        Assert.Equal("9223372036854775807", flags["limits.pro.maxZipSize"]);
        Assert.Equal("30", flags["limits.pro.retentionDays"]);
        Assert.Equal("3", flags["limits.pro.graceDays"]);
        Assert.Equal("1000", flags["limits.pro.maxDownloads"]);
        Assert.Equal("100", flags["limits.pro.maxEmails"]);
        Assert.Equal("107374182400", flags["limits.pro.storageQuota"]);
        Assert.Equal("200", flags["limits.pro.activeTransfersMax"]);
        Assert.Equal("true", flags["limits.pro.scheduling"]);
        Assert.Equal("true", flags["limits.pro.branding"]);
        Assert.Equal("true", flags["limits.pro.analytics"]);
        Assert.Equal("false", flags["limits.pro.ads"]);
        Assert.Equal("false", flags["limits.pro.ssoScim"]);

        // business
        Assert.Equal("107374182400", flags["limits.business.maxTransferSize"]);
        Assert.Equal("107374182400", flags["limits.business.maxSingleFile"]);
        Assert.Equal("9223372036854775807", flags["limits.business.maxZipSize"]);
        Assert.Equal("90", flags["limits.business.retentionDays"]);
        Assert.Equal("3", flags["limits.business.graceDays"]);
        Assert.Equal("-1", flags["limits.business.maxDownloads"]);
        Assert.Equal("500", flags["limits.business.maxEmails"]);
        Assert.Equal("1099511627776", flags["limits.business.storageQuota"]);
        Assert.Equal("-1", flags["limits.business.activeTransfersMax"]);
        Assert.Equal("true", flags["limits.business.scheduling"]);
        Assert.Equal("true", flags["limits.business.branding"]);
        Assert.Equal("true", flags["limits.business.analytics"]);
        Assert.Equal("false", flags["limits.business.ads"]);
        Assert.Equal("true", flags["limits.business.ssoScim"]);
    }
}