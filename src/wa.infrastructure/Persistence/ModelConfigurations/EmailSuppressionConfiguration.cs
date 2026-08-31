using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> e)
    {
        e.ToTable("EmailSuppression", "dbo");
        // DDL: CONSTRAINT PK_ES PRIMARY KEY.
        e.HasKey(k => k.Id).HasName("PK_ES");

        e.Property(s => s.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(s => s.Address).HasColumnName("Address").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired();
        e.Property(s => s.SenderEmail).HasColumnName("SenderEmail").HasColumnType("varchar(320)").HasMaxLength(320);
        e.Property(s => s.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");

        e.HasIndex(i => new { i.Address, i.SenderEmail }).IsUnique().HasDatabaseName("UQ_ES");
    }
}
