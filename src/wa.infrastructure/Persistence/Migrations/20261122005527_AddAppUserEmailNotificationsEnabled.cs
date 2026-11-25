using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppUserEmailNotificationsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotificationsEnabled",
                table: "AppUser",
                type: "BIT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailNotificationsEnabled",
                table: "AppUser");
        }
    }
}
