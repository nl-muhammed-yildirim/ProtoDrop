using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.StripeEvent (webhook idempotency — string PK on the Stripe event ID).</summary>
public sealed class StripeEventConfiguration : IEntityTypeConfiguration<StripeEvent>
{
    public void Configure(EntityTypeBuilder<StripeEvent> b)
    {
        b.ToTable("StripeEvent");
        // VARCHAR(100) NOT NULL CONSTRAINT PK_StripeEvent PRIMARY KEY.
        b.HasKey(e => e.StripeEventId).HasName("PK_StripeEvent");
        b.Property(e => e.StripeEventId).HasColumnType("VARCHAR(100)").IsRequired();
    }
}
