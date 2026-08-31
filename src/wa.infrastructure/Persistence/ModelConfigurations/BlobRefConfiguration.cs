using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class BlobRefConfiguration : IEntityTypeConfiguration<BlobRef>
{
    public void Configure(EntityTypeBuilder<BlobRef> e)
    {
        e.ToTable("BlobRef", "dbo");
        e.HasKey(k => k.Id);

        e.Property(b => b.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(b => b.BlobPath).HasColumnName("BlobPath").HasColumnType("varchar(400)").HasMaxLength(400).IsRequired();
        e.HasIndex(i => i.BlobPath).IsUnique().HasDatabaseName("UQ_BlobRef_Path");
        e.Property(b => b.SizeBytes).HasColumnName("SizeBytes").HasColumnType("bigint");
        e.Property(b => b.RefCount).HasColumnName("RefCount").HasColumnType("int").HasDefaultValue(1);
        e.Property(b => b.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        e.Property(b => b.PhysicallyDeletedAtUtc).HasColumnName("PhysicallyDeletedAtUtc").HasColumnType("datetime2");

        // DDL: CREATE INDEX IX_BlobRef_Cleanup ... WHERE RefCount=0 AND PhysicallyDeletedAtUtc IS NULL.
        // EF's WhereSql only accepts SQL fragments without parameter types.
        e.HasIndex(i => i.PhysicallyDeletedAtUtc)
            .HasFilter("RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL")
            .HasDatabaseName("IX_BlobRef_Cleanup");
    }
}
