using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> e)
    {
        e.ToTable("IdempotencyKey", "dbo");
        // DDL: CONSTRAINT PK_Idem PRIMARY KEY.
        e.HasKey(k => k.Key).HasName("PK_Idem");

        e.Property(k => k.Key).HasColumnName("Key").HasColumnType("varchar(128)").HasMaxLength(128).IsRequired();
        e.Property(k => k.ResultJson).HasColumnName("ResultJson").HasColumnType("nvarchar(max)");
        e.Property(k => k.TransferId).HasColumnName("TransferId").HasColumnType("uniqueidentifier");
        e.Property(k => k.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        e.Property(k => k.ExpiresAtUtc).HasColumnName("ExpiresAtUtc").HasColumnType("datetime2");
    }
}
