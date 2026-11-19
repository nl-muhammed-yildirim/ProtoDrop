using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxLogoSizeLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "FeatureFlag",
                columns: new[] { "Key", "Description", "UpdatedAtUtc", "Value" },
                values: new object[,]
                {
                    { "limits.business.maxLogoSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1048576" },
                    { "limits.free.maxLogoSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1048576" },
                    { "limits.pro.maxLogoSize", "Plan limits override (TA-3.4); values per Open-Decisions Part 2", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "1048576" }
                });

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000001"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20,\"maxLogoSize\":1048576}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200,\"maxLogoSize\":1048576}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000003"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1,\"maxLogoSize\":1048576}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.business.maxLogoSize");

            migrationBuilder.DeleteData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.free.maxLogoSize");

            migrationBuilder.DeleteData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxLogoSize");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000001"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":5368709120,\"maxSingleFile\":5368709120,\"maxZipSize\":4294967296,\"retentionDays\":7,\"graceDays\":3,\"maxDownloads\":100,\"maxEmails\":20,\"storageQuota\":5368709120,\"activeTransfersMax\":20}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000003"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":107374182400,\"maxSingleFile\":107374182400,\"maxZipSize\":9223372036854775807,\"retentionDays\":90,\"graceDays\":3,\"maxDownloads\":-1,\"maxEmails\":500,\"storageQuota\":1099511627776,\"activeTransfersMax\":-1}");
        }
    }
}
