using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.AuditLog.</summary>
public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLog");
        // BIGINT IDENTITY(1,1) — server-generated (EF: long PK + ValueGeneratedOnAdd = bigint identity by convention).
        b.HasKey(l => l.Id).HasName("PK_Audit");
        b.Property(l => l.Id).ValueGeneratedOnAdd();

        b.Property(l => l.ActorEmail).HasColumnType("VARCHAR(320)");
        b.Property(l => l.Action).HasColumnType("VARCHAR(100)").IsRequired();
        b.Property(l => l.EntityType).HasColumnType("VARCHAR(64)").IsRequired();
        b.Property(l => l.EntityId).HasColumnType("VARCHAR(100)");
        b.Property(l => l.DetailsJson).HasColumnType("NVARCHAR(MAX)");
    }
}
