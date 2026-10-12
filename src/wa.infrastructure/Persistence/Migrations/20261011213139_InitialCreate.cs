using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa.infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorEmail = table.Column<string>(type: "VARCHAR(320)", nullable: true),
                    Action = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    EntityType = table.Column<string>(type: "VARCHAR(64)", nullable: false),
                    EntityId = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DetailsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthToken",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(320)", nullable: false),
                    TokenHash = table.Column<string>(type: "CHAR(64)", nullable: false),
                    Purpose = table.Column<byte>(type: "TINYINT", nullable: false),
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
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobPath = table.Column<string>(type: "VARCHAR(400)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RefCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                        .Annotation("Relational:DefaultConstraintName", "DF_BlobRef_C"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PhysicallyDeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlobRef", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IpHash = table.Column<string>(type: "VARCHAR(64)", nullable: true),
                    Country = table.Column<string>(type: "CHAR(2)", nullable: true),
                    UaHash = table.Column<string>(type: "VARCHAR(64)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownEvent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSuppression",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "VARCHAR(320)", nullable: false),
                    SenderEmail = table.Column<string>(type: "VARCHAR(320)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlag",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Value = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Description = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flag", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyKey",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(128)", nullable: false),
                    ResultJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
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
                columns: table => new
                {
                    JobKey = table.Column<string>(type: "VARCHAR(64)", nullable: false),
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
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(32)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(64)", nullable: false),
                    LimitsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    FeaturesJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                        .Annotation("Relational:DefaultConstraintName", "DF_Plan_Sort")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StripeEvent",
                columns: table => new
                {
                    StripeEventId = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeEvent", x => x.StripeEventId);
                });

            migrationBuilder.CreateTable(
                name: "AppUser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(320)", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                        .Annotation("Relational:DefaultConstraintName", "DF_AppUser_Conf"),
                    DisplayName = table.Column<string>(type: "NVARCHAR(100)", nullable: false),
                    PasswordHash = table.Column<string>(type: "VARCHAR(256)", nullable: true),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Theme = table.Column<string>(type: "VARCHAR(16)", nullable: false, defaultValue: "system")
                        .Annotation("Relational:DefaultConstraintName", "DF_AppUser_Theme"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUser_Plan",
                        column: x => x.PlanId,
                        principalTable: "Plan",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Subscription",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeSubId = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    PlanCode = table.Column<string>(type: "VARCHAR(32)", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(32)", nullable: false),
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
                        principalTable: "AppUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transfer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<string>(type: "CHAR(8)", nullable: false),
                    OwnerAppUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<byte>(type: "TINYINT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxDownloads = table.Column<int>(type: "int", nullable: false),
                    DownloadsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                        .Annotation("Relational:DefaultConstraintName", "DF_T_Downloads"),
                    PasswordHash = table.Column<string>(type: "VARCHAR(256)", nullable: true),
                    SenderName = table.Column<string>(type: "NVARCHAR(100)", nullable: false),
                    SenderEmail = table.Column<string>(type: "VARCHAR(320)", nullable: true),
                    Note = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    ScheduledSendAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                        .Annotation("Relational:DefaultConstraintName", "DF_T_Bytes"),
                    FileCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                        .Annotation("Relational:DefaultConstraintName", "DF_T_FileCount"),
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
                        principalTable: "Transfer",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transfer_Owner",
                        column: x => x.OwnerAppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EmailRecipient",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "VARCHAR(320)", nullable: false),
                    NotifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ER", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ER_T",
                        column: x => x.TransferId,
                        principalTable: "Transfer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FileItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlobRefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalName = table.Column<string>(type: "NVARCHAR(500)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileItem_B",
                        column: x => x.BlobRefId,
                        principalTable: "BlobRef",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FileItem_T",
                        column: x => x.TransferId,
                        principalTable: "Transfer",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UQ_AppUser_Email",
                table: "AppUser",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_AuthToken_Hash",
                table: "AuthToken",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlobRef_Cleanup",
                table: "BlobRef",
                column: "PhysicallyDeletedAtUtc",
                filter: "RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_BlobRef_Path",
                table: "BlobRef",
                column: "BlobPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownEvent_T",
                table: "DownloadEvent",
                columns: new[] { "TransferId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_ER",
                table: "EmailRecipient",
                columns: new[] { "TransferId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_ES",
                table: "EmailSuppression",
                columns: new[] { "Address", "SenderEmail" },
                unique: true,
                filter: "[SenderEmail] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileItem_T",
                table: "FileItem",
                columns: new[] { "TransferId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UQ_FileItem_T_B",
                table: "FileItem",
                columns: new[] { "TransferId", "BlobRefId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Plan_Code",
                table: "Plan",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Sub_Stripe",
                table: "Subscription",
                column: "StripeSubId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Expiry",
                table: "Transfer",
                columns: new[] { "Status", "ExpiresAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Owner",
                table: "Transfer",
                columns: new[] { "OwnerAppUserId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_Sched",
                table: "Transfer",
                columns: new[] { "Status", "ScheduledSendAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UQ_Transfer_LinkId",
                table: "Transfer",
                column: "LinkId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "AuthToken");

            migrationBuilder.DropTable(
                name: "DownloadEvent");

            migrationBuilder.DropTable(
                name: "EmailRecipient");

            migrationBuilder.DropTable(
                name: "EmailSuppression");

            migrationBuilder.DropTable(
                name: "FeatureFlag");

            migrationBuilder.DropTable(
                name: "FileItem");

            migrationBuilder.DropTable(
                name: "IdempotencyKey");

            migrationBuilder.DropTable(
                name: "JobRun");

            migrationBuilder.DropTable(
                name: "StripeEvent");

            migrationBuilder.DropTable(
                name: "Subscription");

            migrationBuilder.DropTable(
                name: "BlobRef");

            migrationBuilder.DropTable(
                name: "Transfer");

            migrationBuilder.DropTable(
                name: "AppUser");

            migrationBuilder.DropTable(
                name: "Plan");
        }
    }
}
