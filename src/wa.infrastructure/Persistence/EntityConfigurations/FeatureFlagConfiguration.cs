using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.FeatureFlag (string PK on Key).</summary>
public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    private static readonly DateTime SeedUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<FeatureFlag> b)
    {
        b.ToTable("FeatureFlag");
        // VARCHAR(100) NOT NULL CONSTRAINT PK_Flag PRIMARY KEY.
        b.HasKey(f => f.Key).HasName("PK_Flag");
        b.Property(f => f.Key).HasColumnType("VARCHAR(100)").IsRequired();

        b.Property(f => f.Value).HasColumnType("NVARCHAR(MAX)").IsRequired();
        b.Property(f => f.Description).HasColumnType("NVARCHAR(500)");

        // T-052-04 seeds (FR-052-4): 12 feature.* gates (default false per D-14) + 3 x 14 limits.<plan>.* keys.
        b.HasData(
            Gate("feature.ads", "Free-tier ads on/off (D-14: off at MVP launch)"),
            Gate("feature.albums", "Albums global gate (Phase 2c)"),
            Gate("feature.analytics", "Download analytics global gate"),
            Gate("feature.branding", "Branding global gate"),
            Gate("feature.collect", "Collect global gate (Phase 2a)"),
            Gate("feature.dataExport", "GDPR data export gate (F-XCT-004)"),
            Gate("feature.emailSettings", "Email settings global gate (F-XCT-001)"),
            Gate("feature.resend", "Re-send global gate"),
            Gate("feature.scheduling", "Scheduled send global gate"),
            Gate("feature.search", "Search global gate (F-XCT-002)"),
            Gate("feature.sign", "Sign global gate (Phase 2b)"),
            Gate("feature.ssoScim", "SSO/SCIM global gate (F-ENT-001, per Part-2 SSO_SCIM)"),
            Limit("limits.business.activeTransfersMax", "-1"),
            Limit("limits.business.ads", "false"),
            Limit("limits.business.analytics", "true"),
            Limit("limits.business.branding", "true"),
            Limit("limits.business.graceDays", "3"),
            Limit("limits.business.maxDownloads", "-1"),
            Limit("limits.business.maxEmails", "500"),
            Limit("limits.business.maxSingleFile", "107374182400"),
            Limit("limits.business.maxTransferSize", "107374182400"),
            Limit("limits.business.maxZipSize", "9223372036854775807"),
            Limit("limits.business.retentionDays", "90"),
            Limit("limits.business.scheduling", "true"),
            Limit("limits.business.ssoScim", "true"),
            Limit("limits.business.storageQuota", "1099511627776"),
            Limit("limits.free.activeTransfersMax", "20"),
            Limit("limits.free.ads", "false"),
            Limit("limits.free.analytics", "false"),
            Limit("limits.free.branding", "false"),
            Limit("limits.free.graceDays", "3"),
            Limit("limits.free.maxDownloads", "100"),
            Limit("limits.free.maxEmails", "20"),
            Limit("limits.free.maxSingleFile", "5368709120"),
            Limit("limits.free.maxTransferSize", "5368709120"),
            Limit("limits.free.maxZipSize", "4294967296"),
            Limit("limits.free.retentionDays", "7"),
            Limit("limits.free.scheduling", "false"),
            Limit("limits.free.ssoScim", "false"),
            Limit("limits.free.storageQuota", "5368709120"),
            // D-07 stand-in (ESCALATIONS.md E-004): Pro maxTransferSize 10 GB until D-07 is answered.
            Limit("limits.pro.activeTransfersMax", "200"),
            Limit("limits.pro.ads", "false"),
            Limit("limits.pro.analytics", "true"),
            Limit("limits.pro.branding", "true"),
            Limit("limits.pro.graceDays", "3"),
            Limit("limits.pro.maxDownloads", "1000"),
            Limit("limits.pro.maxEmails", "100"),
            Limit("limits.pro.maxSingleFile", "10737418240"),
            Limit("limits.pro.maxTransferSize", "10737418240"),
            Limit("limits.pro.maxZipSize", "9223372036854775807"),
            Limit("limits.pro.retentionDays", "30"),
            Limit("limits.pro.scheduling", "true"),
            Limit("limits.pro.ssoScim", "false"),
            Limit("limits.pro.storageQuota", "107374182400"));
    }

    private static FeatureFlag Gate(string key, string description) => new()
        { Key = key, Value = "false", Description = description, UpdatedAtUtc = SeedUtc };

    private static FeatureFlag Limit(string key, string value) => new()
        { Key = key, Value = value, Description = "Plan limits override (TA-3.4); values per Open-Decisions Part 2", UpdatedAtUtc = SeedUtc };
}
