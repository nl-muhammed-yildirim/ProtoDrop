using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.Plan.</summary>
public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plan");
        b.HasKey(p => p.Id).HasName("PK_Plan");

        b.Property(p => p.Code).HasColumnType("VARCHAR(32)").IsRequired();
        b.HasIndex(p => p.Code).IsUnique().HasDatabaseName("UQ_Plan_Code");

        b.Property(p => p.Name).HasColumnType("VARCHAR(64)").IsRequired();
        b.Property(p => p.LimitsJson).HasColumnType("NVARCHAR(MAX)");
        b.Property(p => p.FeaturesJson).HasColumnType("NVARCHAR(MAX)");
        var sortOrder = b.Property(p => p.SortOrder);
        sortOrder.HasDefaultValue(0);
        sortOrder.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_Plan_Sort");

        // T-052-04 seeds (FR-052-4): fixed GUIDs keep FK values stable across environments (TA-14.3).
        b.HasData(
            new Plan { Id = Guid.Parse("11111111-0000-4000-8000-000000000001"), Code = "free", Name = "Free", SortOrder = 1,
                LimitsJson = "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}",
                FeaturesJson = "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}" },
            new Plan { Id = Guid.Parse("11111111-0000-4000-8000-000000000002"), Code = "pro", Name = "Pro", SortOrder = 2,
                // D-07 stand-in (ESCALATIONS.md E-004): maxTransferSize 10 GB until D-07 is answered.
                LimitsJson = "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}",
                FeaturesJson = "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":false}" },
            new Plan { Id = Guid.Parse("11111111-0000-4000-8000-000000000003"), Code = "business", Name = "Business", SortOrder = 3,
                LimitsJson = "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}",
                FeaturesJson = "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":true}" });
    }
}
