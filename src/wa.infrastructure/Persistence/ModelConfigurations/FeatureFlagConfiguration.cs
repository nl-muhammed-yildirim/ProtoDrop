using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> e)
    {
        e.ToTable("FeatureFlag", "dbo");
        // DDL: CONSTRAINT PK_Flag PRIMARY KEY.
        e.HasKey(k => k.Key).HasName("PK_Flag");

        e.Property(f => f.Key).HasColumnName("Key").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(f => f.Value).HasColumnName("Value").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(f => f.Description).HasColumnName("Description").HasColumnType("nvarchar(500)").HasMaxLength(500);
        e.Property(f => f.UpdatedAtUtc).HasColumnName("UpdatedAtUtc").HasColumnType("datetime2");
    }
}
