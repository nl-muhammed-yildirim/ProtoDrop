using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.DownloadEvent + IX_DownEvent_T.</summary>
public sealed class DownloadEventConfiguration : IEntityTypeConfiguration<DownloadEvent>
{
    public void Configure(EntityTypeBuilder<DownloadEvent> b)
    {
        b.ToTable("DownloadEvent");
        // BIGINT IDENTITY(1,1) — server-generated (EF: long PK + ValueGeneratedOnAdd = bigint identity by convention).
        b.HasKey(e => e.Id).HasName("PK_DownEvent");
        b.Property(e => e.Id).ValueGeneratedOnAdd();

        // NULL = download-all.
        b.HasIndex(e => new { e.TransferId, e.CreatedAtUtc }).HasDatabaseName("IX_DownEvent_T");

        // VARCHAR(64) — HMAC(ip)/HMAC(UA), PII-safe. CHAR(2) — ISO country code.
        b.Property(e => e.IpHash).HasColumnType("VARCHAR(64)");
        b.Property(e => e.Country).HasColumnType("CHAR(2)");
        b.Property(e => e.UaHash).HasColumnType("VARCHAR(64)");
    }
}
