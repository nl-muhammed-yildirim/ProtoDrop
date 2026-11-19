using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferStatusScheduled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "Transfer",
                type: "TINYINT",
                nullable: false,
                comment: "0 Draft,1 Active,2 Expired,3 DownloadLimit,4 Deleted,5 Scheduled",
                oldClrType: typeof(byte),
                oldType: "TINYINT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "Transfer",
                type: "TINYINT",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "TINYINT",
                oldComment: "0 Draft,1 Active,2 Expired,3 DownloadLimit,4 Deleted,5 Scheduled");
        }
    }
}
