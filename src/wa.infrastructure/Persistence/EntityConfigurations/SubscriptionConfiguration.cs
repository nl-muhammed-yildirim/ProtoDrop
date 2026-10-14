using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.Subscription (billing mirror, P1).</summary>
public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscription");
        b.HasKey(s => s.Id).HasName("PK_Sub");

        // FK_Sub_U -> dbo.AppUser(Id); DDL has no ON DELETE clause = SQL Server default NO ACTION (EC-052-1).
        b.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(s => s.AppUserId)
            .HasConstraintName("FK_Sub_U")
            .OnDelete(DeleteBehavior.NoAction);

        // TA-3.2 defines no index on AppUserId — the automatic FK index is suppressed via
        // ForeignKeyIndexConvention removal in WaDbContext.ConfigureConventions (no per-entity API exists).

        // VARCHAR(100) NOT NULL CONSTRAINT UQ_Sub_Stripe UNIQUE — Stripe sub id is unique per mirror row.
        b.Property(s => s.StripeSubId).HasColumnType("VARCHAR(100)").IsRequired();
        b.HasIndex(s => s.StripeSubId).IsUnique().HasDatabaseName("UQ_Sub_Stripe");

        b.Property(s => s.StripeCustomerId).HasColumnType("VARCHAR(100)");
        // VARCHAR(32) NOT NULL — active|trialing|past_due|paused|canceled.
        b.Property(s => s.PlanCode).HasColumnType("VARCHAR(32)").IsRequired();
        b.Property(s => s.Status).HasColumnType("VARCHAR(32)").IsRequired();
    }
}
