using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class FileItemConfiguration : IEntityTypeConfiguration<FileItem>
{
    public void Configure(EntityTypeBuilder<FileItem> e)
    {
        e.ToTable("FileItem", "dbo");
        e.HasKey(k => k.Id);

        e.Property(f => f.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(f => f.TransferId).HasColumnName("TransferId").HasColumnType("uniqueidentifier").IsRequired();
        e.Property(f => f.BlobRefId).HasColumnName("BlobRefId").HasColumnType("uniqueidentifier").IsRequired();
        e.Property(f => f.OriginalName).HasColumnName("OriginalName").HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        e.Property(f => f.SizeBytes).HasColumnName("SizeBytes").HasColumnType("bigint").IsRequired();
        e.Property(f => f.ContentType).HasColumnName("ContentType").HasColumnType("varchar(200)").HasMaxLength(200);
        e.Property(f => f.SortOrder).HasColumnName("SortOrder").HasColumnType("int");

        // DDL: CONSTRAINT FK_FileItem_T REFERENCES dbo.Transfer(Id);
        //      CONSTRAINT FK_FileItem_B REFERENCES dbo.BlobRef(Id).
        // DDL: no ON DELETE clause -> NO ACTION; EF defaults required->CASCADE, optional->SET NULL.
        e.HasOne<Transfer>().WithMany().HasForeignKey(f => f.TransferId)
            .HasConstraintName("FK_FileItem_T")
            .OnDelete(DeleteBehavior.NoAction);
        e.HasOne<BlobRef>().WithMany().HasForeignKey(f => f.BlobRefId)
            .HasConstraintName("FK_FileItem_B")
            .OnDelete(DeleteBehavior.NoAction);

        e.HasIndex(i => new { i.TransferId, i.BlobRefId }).IsUnique().HasDatabaseName("UQ_FileItem_T_B");
        e.HasIndex(i => new { i.TransferId, i.SortOrder }).HasDatabaseName("IX_FileItem_T");
    }
}