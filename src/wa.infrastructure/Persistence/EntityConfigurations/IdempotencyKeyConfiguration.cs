using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.IdempotencyKey — string PK on the client-supplied Key (TA-4.1.5).</summary>
public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("IdempotencyKey");
        // VARCHAR(128) NOT NULL CONSTRAINT PK_Idem PRIMARY KEY.
        b.HasKey(k => k.Key).HasName("PK_Idem");
        b.Property(k => k.Key).HasColumnType("VARCHAR(128)").IsRequired();

        b.Property(k => k.ResultJson).HasColumnType("NVARCHAR(MAX)");
    }
}
