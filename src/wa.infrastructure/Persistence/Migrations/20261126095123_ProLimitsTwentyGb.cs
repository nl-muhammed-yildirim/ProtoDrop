using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProLimitsTwentyGb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxSingleFile",
                column: "Value",
                value: "21474836480");

            migrationBuilder.UpdateData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxTransferSize",
                column: "Value",
                value: "21474836480");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":21474836480,\"maxSingleFile\":21474836480,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200,\"maxLogoSize\":1048576}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxSingleFile",
                column: "Value",
                value: "10737418240");

            migrationBuilder.UpdateData(
                table: "FeatureFlag",
                keyColumn: "Key",
                keyValue: "limits.pro.maxTransferSize",
                column: "Value",
                value: "10737418240");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"),
                column: "LimitsJson",
                value: "{\"maxTransferSize\":10737418240,\"maxSingleFile\":10737418240,\"maxZipSize\":9223372036854775807,\"retentionDays\":30,\"graceDays\":3,\"maxDownloads\":1000,\"maxEmails\":100,\"storageQuota\":107374182400,\"activeTransfersMax\":200,\"maxLogoSize\":1048576}");
        }
    }
}
