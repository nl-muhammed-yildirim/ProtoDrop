using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace wa.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedPlansAndFeatureFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "dbo",
                table: "FeatureFlag",
                columns: new[] { "Key", "Description", "UpdatedAtUtc", "Value" },
                values: new object[,]
                {
                    { "feature.ads", "Free-tier ads on/off (D-14: off at MVP launch)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.albums", "Albums global gate (Phase 2c)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.analytics", "Download analytics global gate", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.branding", "Branding global gate", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.collect", "Collect global gate (Phase 2a)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.dataExport", "GDPR data export gate (F-XCT-004)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.emailSettings", "Email settings global gate (F-XCT-001)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.resend", "Re-send global gate", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.scheduling", "Scheduled send global gate", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.search", "Search global gate (F-XCT-002)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.sign", "Sign global gate (Phase 2b)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "feature.ssoScim", "SSO/SCIM global gate (F-ENT-001, per Part-2 SSO_SCIM)", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.business.activeTransfersMax", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "-1" },
                    { "limits.business.ads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.business.analytics", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.business.branding", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.business.graceDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { "limits.business.maxDownloads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "-1" },
                    { "limits.business.maxEmails", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "500" },
                    { "limits.business.maxSingleFile", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "107374182400" },
                    { "limits.business.maxTransferSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "107374182400" },
                    { "limits.business.maxZipSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "9223372036854775807" },
                    { "limits.business.retentionDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "90" },
                    { "limits.business.scheduling", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.business.ssoScim", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.business.storageQuota", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1099511627776" },
                    { "limits.free.activeTransfersMax", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "20" },
                    { "limits.free.ads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.free.analytics", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.free.branding", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.free.graceDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { "limits.free.maxDownloads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "100" },
                    { "limits.free.maxEmails", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "20" },
                    { "limits.free.maxSingleFile", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5368709120" },
                    { "limits.free.maxTransferSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5368709120" },
                    { "limits.free.maxZipSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "4294967296" },
                    { "limits.free.retentionDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "7" },
                    { "limits.free.scheduling", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.free.ssoScim", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.free.storageQuota", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "5368709120" },
                    { "limits.pro.activeTransfersMax", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "200" },
                    { "limits.pro.ads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.pro.analytics", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.pro.branding", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.pro.graceDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "3" },
                    { "limits.pro.maxDownloads", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1000" },
                    { "limits.pro.maxEmails", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "100" },
                    { "limits.pro.maxSingleFile", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "21474836480" },
                    { "limits.pro.maxTransferSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "21474836480" },
                    { "limits.pro.maxZipSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "9223372036854775807" },
                    { "limits.pro.retentionDays", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "30" },
                    { "limits.pro.scheduling", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "limits.pro.ssoScim", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "limits.pro.storageQuota", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "107374182400" }
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Plan",
                columns: new[] { "Id", "Code", "FeaturesJson", "LimitsJson", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-4000-8000-000000000001"), "free", "{\"ads\":false,\"scheduling\":false,\"analytics\":false,\"branding\":false,\"sso_scim\":false}", "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}", "Free", 1 },
                    { new Guid("11111111-0000-4000-8000-000000000002"), "pro", "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":false}", "{\"maxTransferSize\":21474836480,\"maxSingleFile\":21474836480,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}", "Pro", 2 },
                    { new Guid("11111111-0000-4000-8000-000000000003"), "business", "{\"ads\":false,\"scheduling\":true,\"analytics\":true,\"branding\":true,\"sso_scim\":true}", "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}", "Business", 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.ads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.albums");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.analytics");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.branding");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.collect");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.dataExport");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.emailSettings");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.resend");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.scheduling");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.search");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.sign");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "feature.ssoScim");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.activeTransfersMax");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.ads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.analytics");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.branding");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.graceDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxDownloads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxEmails");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxSingleFile");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxTransferSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxZipSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.retentionDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.scheduling");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.ssoScim");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.storageQuota");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.activeTransfersMax");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.ads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.analytics");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.branding");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.graceDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxDownloads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxEmails");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxSingleFile");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxTransferSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxZipSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.retentionDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.scheduling");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.ssoScim");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.storageQuota");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.activeTransfersMax");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.ads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.analytics");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.branding");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.graceDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxDownloads");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxEmails");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxSingleFile");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxTransferSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxZipSize");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.retentionDays");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.scheduling");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.ssoScim");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.storageQuota");

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"));

            migrationBuilder.DeleteData(
                schema: "dbo",
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000003"));
        }
    }
}
