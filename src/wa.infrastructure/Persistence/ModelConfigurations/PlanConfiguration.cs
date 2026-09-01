using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> e)
    {
        e.ToTable("Plan", "dbo");
        e.HasKey(k => k.Id);

        e.Property(p => p.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(p => p.Code).HasColumnName("Code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        e.HasIndex(i => i.Code).IsUnique().HasDatabaseName("UQ_Plan_Code");
        e.Property(p => p.Name).HasColumnName("Name").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        e.Property(p => p.LimitsJson).HasColumnName("LimitsJson").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(p => p.FeaturesJson).HasColumnName("FeaturesJson").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(p => p.SortOrder).HasColumnName("SortOrder").HasColumnType("int").HasDefaultValue(0);

        // TA-3.7 / F-BIL-001: Plan rows are data. Fixed GUIDs so FKs (AppUser.PlanId etc.)
        // are portable across environments. Limits/flags per Open-Decisions-and-Constants
        // Part 2 (free = D-06 confirmed; pro/business pending D-07/D-19 per EC-018-1).
        e.HasData(
            new Plan
            {
                Id = new Guid("11111111-0000-4000-8000-000000000001"),
                Code = "free",
                Name = "Free",
                LimitsJson = LimitsFree,
                FeaturesJson = FeaturesFree,
                SortOrder = 1
            },
            new Plan
            {
                Id = new Guid("11111111-0000-4000-8000-000000000002"),
                Code = "pro",
                Name = "Pro",
                LimitsJson = LimitsPro,
                FeaturesJson = FeaturesPro,
                SortOrder = 2
            },
            new Plan
            {
                Id = new Guid("11111111-0000-4000-8000-000000000003"),
                Code = "business",
                Name = "Business",
                LimitsJson = LimitsBusiness,
                FeaturesJson = FeaturesBusiness,
                SortOrder = 3
            });
    }

    // JSON key contracts (consumed by T-005 ILimitsProvider + T-004a parsing):
    //   LimitsJson  — Part-2 hard limits, camelCase: maxTransferSize, maxSingleFile,
    //                 maxZipSize, retentionDays, graceDays, maxDownloads, maxEmails,
    //                 storageQuota, activeTransfersMax. maxDownloads = -1 → unlimited.
    //   FeaturesJson — F-BIL-001 feature gates: ads, scheduling, analytics, branding,
    //                 sso_scim (F-BIL-001 shape; note snake_case key).
    // Values per Open-Decisions-and-Constants.md Part 2 (Appendix A / D-06 frozen).
    // Bytes: 5GB=5368709120, 20GB=21474836480, 100GB=107374182400, 1TB=1099511627776,
    // 4GB=4294967296. Pro maxTransferSize 20 GB is the Appendix-A placeholder pending D-07.
    private const string LimitsFree =
        "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}";
    private const string FeaturesFree =
        "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}";
    private const string LimitsPro =
        "{\"maxTransferSize\":21474836480,\"maxSingleFile\":21474836480,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}";
    private const string FeaturesPro =
        "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":false}";
    private const string LimitsBusiness =
        "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}";
    private const string FeaturesBusiness =
        "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":true}";
}
