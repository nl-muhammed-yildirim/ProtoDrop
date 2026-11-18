using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>US-047-01: dbo.DataExport (GDPR export requests). No FK to AppUser — soft-deleted users keep their rows (EC-047-3).</summary>
public sealed class DataExportConfiguration : IEntityTypeConfiguration<DataExport>
{
    public void Configure(EntityTypeBuilder<DataExport> b)
    {
        b.ToTable("DataExport");
        b.HasKey(e => e.Id).HasName("PK_DataExport");

        b.Property(e => e.State).HasColumnType("TINYINT");
        // BlobRef path width (VARCHAR(400)).
        b.Property(e => e.BlobPath).HasColumnType("VARCHAR(400)");

        // One in-flight export per user, enforced by SQL (EC-047-6 race-proof).
        b.HasIndex(e => e.AppUserId)
            .IsUnique()
            .HasFilter("[State] = 0")
            .HasDatabaseName("UX_DataExport_InFlight");
        b.HasIndex(e => new { e.AppUserId, e.CreatedAtUtc }).HasDatabaseName("IX_DataExport_User");
        b.HasIndex(e => new { e.State, e.CreatedAtUtc }).HasDatabaseName("IX_DataExport_Scan");
    }
}
