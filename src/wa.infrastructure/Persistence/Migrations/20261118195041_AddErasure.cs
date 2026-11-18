using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Erasure",
                columns: table => new
                {
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReHomedTransferCount = table.Column<int>(type: "int", nullable: false),
                    SweptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Erasure", x => x.AppUserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Erasure_Pending",
                table: "Erasure",
                column: "DeletedAtUtc",
                filter: "[SweptAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Erasure");
        }
    }
}
