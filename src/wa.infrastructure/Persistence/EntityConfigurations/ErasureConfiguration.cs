using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>US-047-02: dbo.Erasure (account-deletion markers). No FK to AppUser — a later hard delete must not erase the erasure record.</summary>
public sealed class ErasureConfiguration : IEntityTypeConfiguration<Erasure>
{
    public void Configure(EntityTypeBuilder<Erasure> b)
    {
        b.ToTable("Erasure");
        b.HasKey(e => e.AppUserId).HasName("PK_Erasure");
        b.Property(e => e.AppUserId).ValueGeneratedNever();

        // Sweep scan: pending markers, oldest first.
        b.HasIndex(e => e.DeletedAtUtc)
            .HasFilter("[SweptAtUtc] IS NULL")
            .HasDatabaseName("IX_Erasure_Pending");
    }
}
