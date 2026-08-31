using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class DownloadEventConfiguration : IEntityTypeConfiguration<DownloadEvent>
{
    public void Configure(EntityTypeBuilder<DownloadEvent> e)
    {
        e.ToTable("DownloadEvent", "dbo");
        // DDL: CONSTRAINT PK_DownEvent PRIMARY KEY.
        e.HasKey(k => k.Id).HasName("PK_DownEvent");

        e.Property(d => d.Id).HasColumnName("Id").HasColumnType("bigint").ValueGeneratedOnAdd();
        e.Property(d => d.TransferId).HasColumnName("TransferId").HasColumnType("uniqueidentifier").IsRequired();
        e.Property(d => d.FileId).HasColumnName("FileId").HasColumnType("uniqueidentifier");
        e.Property(d => d.IpHash).HasColumnName("IpHash").HasColumnType("varchar(64)").HasMaxLength(64);
        e.Property(d => d.Country).HasColumnName("Country").HasColumnType("char(2)").HasMaxLength(2);
        e.Property(d => d.UaHash).HasColumnName("UaHash").HasColumnType("varchar(64)").HasMaxLength(64);
        e.Property(d => d.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");

        e.HasIndex(i => new { i.TransferId, i.CreatedAtUtc }).HasDatabaseName("IX_DownEvent_T");
    }
}
