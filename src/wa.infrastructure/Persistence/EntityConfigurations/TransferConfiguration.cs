using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.Transfer + IX_Transfer_Expiry / IX_Transfer_Owner / IX_Transfer_Sched.</summary>
public sealed class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> b)
    {
        b.ToTable("Transfer");
        b.HasKey(t => t.Id).HasName("PK_Transfer");

        // CHAR(8) NOT NULL CONSTRAINT UQ_Transfer_LinkId UNIQUE (TA-7.1 link ID).
        b.Property(t => t.LinkId).HasColumnType("CHAR(8)").IsRequired();
        b.HasIndex(t => t.LinkId).IsUnique().HasDatabaseName("UQ_Transfer_LinkId");

        // FK_Transfer_Owner -> dbo.AppUser(Id); nullable owner (anonymous drops). NO ACTION default.
        b.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(t => t.OwnerAppUserId)
            .HasConstraintName("FK_Transfer_Owner");

        // TA-3.2 defines no index on OwnerAppUserId alone (only the IX_Transfer_Owner composite below) —
        // the automatic FK index is suppressed via ForeignKeyIndexConvention removal in WaDbContext.ConfigureConventions.

        // TINYINT — 0 Draft, 1 Active, 2 Expired, 3 DownloadLimit, 4 Deleted.
        b.Property(t => t.Status).HasColumnType("TINYINT");
        // INT NOT NULL CONSTRAINT DF_T_Downloads DEFAULT 0 — TA-3.2 names the default constraint explicitly (AC-052-4).
        var downloadsCount = b.Property(t => t.DownloadsCount);
        downloadsCount.HasDefaultValue(0);
        downloadsCount.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_T_Downloads");

        b.Property(t => t.PasswordHash).HasColumnType("VARCHAR(256)"); // plain password never persisted
        b.Property(t => t.SenderName).HasColumnType("NVARCHAR(100)").IsRequired();
        b.Property(t => t.SenderEmail).HasColumnType("VARCHAR(320)");
        b.Property(t => t.Note).HasColumnType("NVARCHAR(500)");

        // BIGINT NOT NULL CONSTRAINT DF_T_Bytes DEFAULT 0 / INT NOT NULL CONSTRAINT DF_T_FileCount DEFAULT 0 — TA-3.2 names the default constraints explicitly (AC-052-4).
        var totalBytes = b.Property(t => t.TotalBytes);
        totalBytes.HasDefaultValue(0L);
        totalBytes.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_T_Bytes");
        var fileCount = b.Property(t => t.FileCount);
        fileCount.HasDefaultValue(0);
        fileCount.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_T_FileCount");

        // Self-reference: transfer that superseded this one (re-send). NO ACTION default.
        b.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(t => t.SupersededBy)
            .HasConstraintName("FK_T_Super");

        // TA-3.2 defines no index on SupersededBy — the automatic FK index is suppressed via
        // ForeignKeyIndexConvention removal in WaDbContext.ConfigureConventions (no per-entity API exists).
        // (Status, ExpiresAtUtc) INCLUDE Id — expiry sweep scans status+expiry without heap lookup.
        b.HasIndex(t => new { t.Status, t.ExpiresAtUtc })
            .IncludeProperties(["Id"])
            .HasDatabaseName("IX_Transfer_Expiry");

        // (OwnerAppUserId, CreatedAtUtc DESC) — latest transfers per owner first.
        b.HasIndex(t => new { t.OwnerAppUserId, t.CreatedAtUtc })
            .IsDescending([false, true])
            .HasDatabaseName("IX_Transfer_Owner");

        // (Status, ScheduledSendAtUtc) — scheduler picks due drafts.
        b.HasIndex(t => new { t.Status, t.ScheduledSendAtUtc })
            .HasDatabaseName("IX_Transfer_Sched");
    }
}
