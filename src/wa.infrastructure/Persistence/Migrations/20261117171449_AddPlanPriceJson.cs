using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPriceJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceJson",
                table: "Plan",
                type: "NVARCHAR(MAX)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000001"),
                column: "PriceJson",
                value: "{\"currency\":\"USD\",\"monthly\":{\"amount\":0,\"stripePriceId\":null},\"annual\":{\"amount\":0,\"stripePriceId\":null},\"perSeatMonthly\":null}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000002"),
                column: "PriceJson",
                value: "{\"currency\":\"USD\",\"monthly\":{\"amount\":900,\"stripePriceId\":null},\"annual\":{\"amount\":9000,\"stripePriceId\":null},\"perSeatMonthly\":null}");

            migrationBuilder.UpdateData(
                table: "Plan",
                keyColumn: "Id",
                keyValue: new Guid("11111111-0000-4000-8000-000000000003"),
                column: "PriceJson",
                value: "{\"currency\":\"USD\",\"monthly\":{\"amount\":1900,\"stripePriceId\":null},\"annual\":{\"amount\":19000,\"stripePriceId\":null},\"perSeatMonthly\":1900}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceJson",
                table: "Plan");
        }
    }
}
