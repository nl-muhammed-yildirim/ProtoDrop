using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> e)
    {
        e.ToTable("FeatureFlag", "dbo");
        // DDL: CONSTRAINT PK_Flag PRIMARY KEY.
        e.HasKey(k => k.Key).HasName("PK_Flag");

        e.Property(f => f.Key).HasColumnName("Key").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(f => f.Value).HasColumnName("Value").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(f => f.Description).HasColumnName("Description").HasColumnType("nvarchar(500)").HasMaxLength(500);
        e.Property(f => f.UpdatedAtUtc).HasColumnName("UpdatedAtUtc").HasColumnType("datetime2");

        // TA-13.2 canonical seed list: 12 feature-gate keys (default false per TA-13.2)
        // + limits.{plan}.* override keys — one per Open-Decisions-and-Constants.md
        // Part-2 constant per plan (TA-13.2). Values mirror Part 2 (frozen constants);
        // seed values equal Plan.LimitsJson baseline so T-005 overrides are additive.
        var seededAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var flags = new List<FeatureFlag>();

        flags.Add(Flag("feature.ads", "false", "Free-tier ads on/off (D-14: off at MVP launch)", seededAt));
        flags.Add(Flag("feature.scheduling", "false", "Scheduled send global gate", seededAt));
        flags.Add(Flag("feature.analytics", "false", "Download analytics global gate", seededAt));
        flags.Add(Flag("feature.resend", "false", "Re-send global gate", seededAt));
        flags.Add(Flag("feature.collect", "false", "Collect global gate (Phase 2a)", seededAt));
        flags.Add(Flag("feature.sign", "false", "Sign global gate (Phase 2b)", seededAt));
        flags.Add(Flag("feature.albums", "false", "Albums global gate (Phase 2c)", seededAt));
        flags.Add(Flag("feature.search", "false", "Search global gate (F-XCT-002)", seededAt));
        flags.Add(Flag("feature.dataExport", "false", "GDPR data export gate (F-XCT-004)", seededAt));
        flags.Add(Flag("feature.emailSettings", "false", "Email settings global gate (F-XCT-001)", seededAt));
        flags.Add(Flag("feature.branding", "false", "Branding global gate", seededAt));
        flags.Add(Flag("feature.ssoScim", "false", "SSO/SCIM global gate (F-ENT-001, per Part-2 SSO_SCIM)", seededAt));

        // limits.{plan}.{constant} — per TA-13.2, one key per Open-Decisions-and-
        // Constants.md Part-2 constant per plan. Values mirror Part 2 ("∞" = -1);
        // Pro maxTransferSize is "TBD (D-07)" in Part 2 — seeded 10 GB (10737418240)
        // as D-07 stand-in (ESCALATIONS.md E-004); Pro/Business retention 30/90 days
        // pending D-19.
        const string limitsNote = "Plan limits override (TA-3.4); values per Open-Decisions Part 2";
        void Limits(string plan, params (string, object? Value)[] parts)
        {
            foreach (var (name, value) in parts)
            {
                flags.Add(Flag($"limits.{plan}.{name}", JsonValue(value), limitsNote, seededAt));
            }
        }
        Limits("free",
            ("maxTransferSize", 5368709120), ("maxSingleFile", 5368709120), ("maxZipSize", 4294967296),
            ("retentionDays", 7), ("graceDays", 3), ("maxDownloads", 100),
            ("maxEmails", 20), ("storageQuota", 5368709120), ("activeTransfersMax", 20),
            ("scheduling", false), ("branding", false), ("analytics", false),
            ("ads", false), ("ssoScim", false));
        Limits("pro",
            // D-07 stand-in: 10 GB (Part 2 is "TBD (D-07)"); see ESCALATIONS.md E-004.
            ("maxTransferSize", 10737418240), ("maxSingleFile", 10737418240), ("maxZipSize", 9223372036854775807L),
            ("retentionDays", 30), ("graceDays", 3), ("maxDownloads", 1000),
            ("maxEmails", 100), ("storageQuota", 107374182400), ("activeTransfersMax", 200),
            ("scheduling", true), ("branding", true), ("analytics", true),
            ("ads", false), ("ssoScim", false));
        Limits("business",
            ("maxTransferSize", 107374182400), ("maxSingleFile", 107374182400), ("maxZipSize", 9223372036854775807L),
            ("retentionDays", 90), ("graceDays", 3), ("maxDownloads", -1),
            ("maxEmails", 500), ("storageQuota", 1099511627776), ("activeTransfersMax", -1),
            ("scheduling", true), ("branding", true), ("analytics", true),
            ("ads", false), ("ssoScim", true));

        e.HasData(flags);
    }

    private static FeatureFlag Flag(string key, string value, string description, DateTime seededAt)
        => new() { Key = key, Value = value, Description = description, UpdatedAtUtc = seededAt };

    private static string JsonValue(object? value) => value switch
    {
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => $"\"{s}\"",
        null => "null",
        _ => value.ToString() ?? "null",
    };
}
