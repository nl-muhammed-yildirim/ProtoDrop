using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.FileItem + IX_FileItem_T.</summary>
public sealed class FileItemConfiguration : IEntityTypeConfiguration<FileItem>
{
    public void Configure(EntityTypeBuilder<FileItem> b)
    {
        b.ToTable("FileItem");
        b.HasKey(f => f.Id).HasName("PK_FileItem");

        // FK_FileItem_T -> dbo.Transfer(Id), FK_FileItem_B -> dbo.BlobRef(Id); DDL has no ON DELETE clause = SQL Server default NO ACTION (EC-052-1).
        b.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(f => f.TransferId)
            .HasConstraintName("FK_FileItem_T")
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne<BlobRef>()
            .WithMany()
            .HasForeignKey(f => f.BlobRefId)
            .HasConstraintName("FK_FileItem_B")
            .OnDelete(DeleteBehavior.NoAction);

        // TA-3.2 defines no index on BlobRefId — the automatic FK index is suppressed via
        // ForeignKeyIndexConvention removal in WaDbContext.ConfigureConventions (no per-entity API exists).

        // A transfer may hold the same blob in several slots only via distinct rows: (TransferId, BlobRefId) unique.
        b.HasIndex(f => new { f.TransferId, f.BlobRefId }).IsUnique().HasDatabaseName("UQ_FileItem_T_B");

        b.Property(f => f.OriginalName).HasColumnType("NVARCHAR(500)").IsRequired();
        b.Property(f => f.ContentType).HasColumnType("VARCHAR(200)");

        // (TransferId, SortOrder) — file listing in stored order.
        b.HasIndex(f => new { f.TransferId, f.SortOrder }).HasDatabaseName("IX_FileItem_T");
    }
}
