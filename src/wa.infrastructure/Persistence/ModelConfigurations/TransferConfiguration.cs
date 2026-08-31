using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> e)
    {
        e.ToTable("Transfer", "dbo");
        e.HasKey(k => k.Id);

        e.Property(t => t.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(t => t.LinkId).HasColumnName("LinkId").HasColumnType("char(8)").HasMaxLength(8).IsRequired();
        e.HasIndex(i => i.LinkId).IsUnique().HasDatabaseName("UQ_Transfer_LinkId");
        e.Property(t => t.OwnerAppUserId).HasColumnName("OwnerAppUserId").HasColumnType("uniqueidentifier");
        e.Property(t => t.Status).HasColumnName("Status").HasColumnType("tinyint");
        e.Property(t => t.ExpiresAtUtc).HasColumnName("ExpiresAtUtc").HasColumnType("datetime2");
        e.Property(t => t.MaxDownloads).HasColumnName("MaxDownloads").HasColumnType("int");
        e.Property(t => t.DownloadsCount).HasColumnName("DownloadsCount").HasColumnType("int").HasDefaultValue(0);
        e.Property(t => t.PasswordHash).HasColumnName("PasswordHash").HasColumnType("varchar(256)").HasMaxLength(256);
        e.Property(t => t.SenderName).HasColumnName("SenderName").HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        e.Property(t => t.SenderEmail).HasColumnName("SenderEmail").HasColumnType("varchar(320)").HasMaxLength(320);
        e.Property(t => t.Note).HasColumnName("Note").HasColumnType("nvarchar(500)").HasMaxLength(500);
        e.Property(t => t.ScheduledSendAtUtc).HasColumnName("ScheduledSendAtUtc").HasColumnType("datetime2");
        e.Property(t => t.TotalBytes).HasColumnName("TotalBytes").HasColumnType("bigint").HasDefaultValue(0L);
        e.Property(t => t.FileCount).HasColumnName("FileCount").HasColumnType("int").HasDefaultValue(0);
        e.Property(t => t.SupersededBy).HasColumnName("SupersededBy").HasColumnType("uniqueidentifier");
        e.Property(t => t.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        e.Property(t => t.ExpiredAtUtc).HasColumnName("ExpiredAtUtc").HasColumnType("datetime2");
        e.Property(t => t.DeletedAtUtc).HasColumnName("DeletedAtUtc").HasColumnType("datetime2");

        // DDL: CONSTRAINT FK_Transfer_Owner REFERENCES dbo.AppUser(Id).
        // DDL: no ON DELETE clause -> NO ACTION; EF defaults required->CASCADE, nullable->SET NULL.
        e.HasOne<AppUser>().WithMany().HasForeignKey(t => t.OwnerAppUserId)
            .HasConstraintName("FK_Transfer_Owner")
            .OnDelete(DeleteBehavior.NoAction);

        // DDL: CONSTRAINT FK_T_Super (self-reference) REFERENCES dbo.Transfer(Id).
        e.HasOne<Transfer>().WithMany().HasForeignKey(t => t.SupersededBy)
            .HasConstraintName("FK_T_Super")
            .OnDelete(DeleteBehavior.NoAction);

        // DDL indexes.
        // DDL: IX_Transfer_Expiry ON dbo.Transfer (Status, ExpiresAtUtc) INCLUDE (Id).
        e.HasIndex(i => new { i.Status, i.ExpiresAtUtc })
            .IncludeProperties(new[] { "Id" })
            .HasDatabaseName("IX_Transfer_Expiry");
        e.HasIndex(i => new { i.OwnerAppUserId, i.CreatedAtUtc }).IsDescending(false, true)
            .HasDatabaseName("IX_Transfer_Owner");
        e.HasIndex(i => new { i.Status, i.ScheduledSendAtUtc }).HasDatabaseName("IX_Transfer_Sched");
    }
}
