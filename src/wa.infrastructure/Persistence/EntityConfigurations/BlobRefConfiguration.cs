using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.BlobRef + IX_BlobRef_Cleanup (filtered).</summary>
public sealed class BlobRefConfiguration : IEntityTypeConfiguration<BlobRef>
{
    public void Configure(EntityTypeBuilder<BlobRef> b)
    {
        b.ToTable("BlobRef");
        b.HasKey(r => r.Id).HasName("PK_BlobRef");

        // VARCHAR(400) NOT NULL CONSTRAINT UQ_BlobRef_Path UNIQUE.
        b.Property(r => r.BlobPath).HasColumnType("VARCHAR(400)").IsRequired();
        b.HasIndex(r => r.BlobPath).IsUnique().HasDatabaseName("UQ_BlobRef_Path");

        // INT NOT NULL CONSTRAINT DF_BlobRef_C DEFAULT 1 — ref-count starts at one.
        var refCount = b.Property(r => r.RefCount);
        refCount.HasDefaultValue(1);
        // TA-3.2 names the default constraint DF_BlobRef_C — EF needs an explicit name per column to match verbatim (AC-052-4).
        refCount.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_BlobRef_C");

        // WHERE RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL — cleanup sweep only sees dead blobs.
        b.HasIndex(r => r.PhysicallyDeletedAtUtc)
            .HasFilter("RefCount = 0 AND PhysicallyDeletedAtUtc IS NOT NULL")
            .HasDatabaseName("IX_BlobRef_Cleanup");
    }
}
