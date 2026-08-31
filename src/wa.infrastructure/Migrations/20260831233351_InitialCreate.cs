using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                    Action = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    EntityId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthToken",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<byte>(type: "tinyint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthToken", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlobRef",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobPath = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RefCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PhysicallyDeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobRef", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadEvent",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "char(2)", maxLength: 2, nullable: true),
                    UaHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSuppression",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    SenderEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlag",
                schema: "dbo",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flag", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKey",
                schema: "dbo",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Idem", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "JobRun",
                schema: "dbo",
                columns: table => new
                {
                    JobKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRun", x => new { x.JobKey, x.RunAtUtc });
                });

            migrationBuilder.CreateTable(
                name: "Plan",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LimitsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StripeEvent",
                schema: "dbo",
                columns: table => new
                {
                    StripeEventId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeEvent", x => x.StripeEventId);
                });

            migrationBuilder.CreateTable(
                name: "AppUser",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Theme = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, defaultValue: "system"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUser_Plan",
                        column: x => x.PlanId,
                        principalSchema: "dbo",
                        principalTable: "Plan",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Subscription",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeSubId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    StripeCustomerId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    PlanCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GraceEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sub", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sub_U",
                        column: x => x.AppUserId,
                        principalSchema: "dbo",
                        principalTable: "AppUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transfer",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<string>(type: "char(8)", maxLength: 8, nullable: false),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxDownloads = table.Column<int>(type: "int", nullable: false),
                    DownloadsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PasswordHash = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SenderEmail = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ScheduledSendAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    FileCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SupersededBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_T_Super",
                        column: x => x.SupersededBy,
                        principalSchema: "dbo",
                        principalTable: "Transfer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfer_Owner",
                        column: x => x.OwnerAppUserId,
                        principalSchema: "dbo",
                        principalTable: "AppUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailRecipient",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ER_T",
                        column: x => x.TransferId,
                        principalSchema: "dbo",
                        principalTable: "Transfer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FileItem",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobRefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileItem_B",
                        column: x => x.BlobRefId,
                        principalSchema: "dbo",
                        principalTable: "BlobRef",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileItem_T",
                        column: x => x.TransferId,
                        principalSchema: "dbo",
                        principalTable: "Transfer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUser_PlanId",
                schema: "dbo",
                table: "AppUser",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "UQ_AppUser_Email",
                schema: "dbo",
                table: "AppUser",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AuthToken_Hash",
                schema: "dbo",
                table: "AuthToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlobRef_Cleanup",
                schema: "dbo",
                table: "BlobRef",
                column: "PhysicallyDeletedAtUtc",
                filter: "RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_BlobRef_Path",
                schema: "dbo",
                table: "BlobRef",
                column: "BlobPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownEvent_T",
                schema: "dbo",
                table: "DownloadEvent",
                columns: new[] { "TransferId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_ER",
                schema: "dbo",
                table: "EmailRecipient",
                columns: new[] { "TransferId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ES",
                schema: "dbo",
                table: "EmailSuppression",
                columns: new[] { "Address", "SenderEmail" },
                unique: true,
                filter: "[SenderEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileItem_BlobRefId",
                schema: "dbo",
                table: "FileItem",
                column: "BlobRefId");

            migrationBuilder.CreateIndex(
                name: "IX_FileItem_T",
                schema: "dbo",
                table: "FileItem",
                columns: new[] { "TransferId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_FileItem_T_B",
                schema: "dbo",
                table: "FileItem",
                columns: new[] { "TransferId", "BlobRefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Plan_Code",
                schema: "dbo",
                table: "Plan",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_AppUserId",
                schema: "dbo",
                table: "Subscription",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "UQ_Sub_Stripe",
                schema: "dbo",
                table: "Subscription",
                column: "StripeSubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Expiry",
                schema: "dbo",
                table: "Transfer",
                columns: new[] { "Status", "ExpiresAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Owner",
                schema: "dbo",
                table: "Transfer",
                columns: new[] { "OwnerAppUserId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Sched",
                schema: "dbo",
                table: "Transfer",
                columns: new[] { "Status", "ScheduledSendAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_SupersededBy",
                schema: "dbo",
                table: "Transfer",
                column: "SupersededBy");

            migrationBuilder.CreateIndex(
                name: "UQ_Transfer_LinkId",
                schema: "dbo",
                table: "Transfer",
                column: "LinkId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AuthToken",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "DownloadEvent",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "EmailRecipient",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "EmailSuppression",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FeatureFlag",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "FileItem",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "IdempotencyKey",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "JobRun",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "StripeEvent",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Subscription",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BlobRef",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Transfer",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "AppUser",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Plan",
                schema: "dbo");
        }
    }
}
