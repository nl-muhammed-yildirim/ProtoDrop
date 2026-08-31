using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> e)
    {
        e.ToTable("Subscription", "dbo");
        // DDL: CONSTRAINT PK_Sub PRIMARY KEY.
        e.HasKey(k => k.Id).HasName("PK_Sub");

        e.Property(s => s.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(s => s.AppUserId).HasColumnName("AppUserId").HasColumnType("uniqueidentifier").IsRequired();
        e.Property(s => s.StripeSubId).HasColumnName("StripeSubId").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.HasIndex(i => i.StripeSubId).IsUnique().HasDatabaseName("UQ_Sub_Stripe");
        e.Property(s => s.StripeCustomerId).HasColumnName("StripeCustomerId").HasColumnType("varchar(100)").HasMaxLength(100);
        e.Property(s => s.PlanCode).HasColumnName("PlanCode").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        e.Property(s => s.Status).HasColumnName("Status").HasColumnType("varchar(32)").HasMaxLength(32).IsRequired();
        e.Property(s => s.CurrentPeriodEndUtc).HasColumnName("CurrentPeriodEndUtc").HasColumnType("datetime2");
        e.Property(s => s.GraceEndsAtUtc).HasColumnName("GraceEndsAtUtc").HasColumnType("datetime2");
        e.Property(s => s.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        e.Property(s => s.UpdatedAtUtc).HasColumnName("UpdatedAtUtc").HasColumnType("datetime2");

        // DDL: CONSTRAINT FK_Sub_U REFERENCES dbo.AppUser(Id).
        // DDL: no ON DELETE clause -> NO ACTION; EF defaults required->CASCADE.
        e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.AppUserId)
            .HasConstraintName("FK_Sub_U")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
