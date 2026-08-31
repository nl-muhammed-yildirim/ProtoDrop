using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> e)
    {
        e.ToTable("Plan", "dbo");
        e.HasKey(k => k.Id);

        e.Property(p => p.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(p => p.Code).HasColumnName("Code").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        e.HasIndex(i => i.Code).IsUnique().HasDatabaseName("UQ_Plan_Code");
        e.Property(p => p.Name).HasColumnName("Name").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        e.Property(p => p.LimitsJson).HasColumnName("LimitsJson").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(p => p.FeaturesJson).HasColumnName("FeaturesJson").HasColumnType("nvarchar(max)").IsRequired();
        e.Property(p => p.SortOrder).HasColumnName("SortOrder").HasColumnType("int").HasDefaultValue(0);
    }
}
