using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class StripeEventConfiguration : IEntityTypeConfiguration<StripeEvent>
{
    public void Configure(EntityTypeBuilder<StripeEvent> e)
    {
        e.ToTable("StripeEvent", "dbo");
        e.HasKey(k => k.StripeEventId);

        e.Property(s => s.StripeEventId).HasColumnName("StripeEventId").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(s => s.ProcessedAtUtc).HasColumnName("ProcessedAtUtc").HasColumnType("datetime2");
    }
}
