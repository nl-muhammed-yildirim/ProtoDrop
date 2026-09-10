using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("AuditLog", "dbo");
        // DDL: CONSTRAINT PK_Audit PRIMARY KEY. HasName (relational) sets the PK constraint name.
        e.HasKey(k => k.Id).HasName("PK_Audit");

        e.Property(a => a.Id).HasColumnName("Id").HasColumnType("bigint").ValueGeneratedOnAdd();
        e.Property(a => a.ActorEmail).HasColumnName("ActorEmail").HasColumnType("varchar(320)").HasMaxLength(320);
        e.Property(a => a.Action).HasColumnName("Action").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(a => a.EntityType).HasColumnName("EntityType").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        e.Property(a => a.EntityId).HasColumnName("EntityId").HasColumnType("varchar(100)").HasMaxLength(100);
        e.Property(a => a.DetailsJson).HasColumnName("DetailsJson").HasColumnType("nvarchar(max)");
        e.Property(a => a.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
    }
}