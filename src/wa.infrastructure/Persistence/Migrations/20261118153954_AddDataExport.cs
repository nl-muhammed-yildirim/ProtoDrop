using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataExport",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<byte>(type: "TINYINT", nullable: false),
                    BlobPath = table.Column<string>(type: "VARCHAR(400)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataExport", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataExport_Scan",
                table: "DataExport",
                columns: new[] { "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DataExport_User",
                table: "DataExport",
                columns: new[] { "AppUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_DataExport_InFlight",
                table: "DataExport",
                column: "AppUserId",
                unique: true,
                filter: "[State] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataExport");
        }
    }
}
