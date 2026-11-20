using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserOrgBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrgLogoBlobPath",
                table: "AppUser",
                type: "VARCHAR(400)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrgName",
                table: "AppUser",
                type: "NVARCHAR(60)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrgLogoBlobPath",
                table: "AppUser");

            migrationBuilder.DropColumn(
                name: "OrgName",
                table: "AppUser");
        }
    }
}
