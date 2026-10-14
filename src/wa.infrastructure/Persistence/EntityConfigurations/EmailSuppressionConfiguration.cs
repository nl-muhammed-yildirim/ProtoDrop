using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.EmailSuppression.</summary>
public sealed class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> b)
    {
        b.ToTable("EmailSuppression");
        // Non-standard PK constraint name in DDL: CONSTRAINT PK_ES.
        b.HasKey(s => s.Id).HasName("PK_ES");

        b.Property(s => s.Address).HasColumnType("VARCHAR(320)").IsRequired();
        // NULL = suppress from any sender (UQ_ES allows one NULL per address in SQL).
        b.Property(s => s.SenderEmail).HasColumnType("VARCHAR(320)");

        // One suppression row per (address, sender); SenderEmail nullable.
        b.HasIndex(s => new { s.Address, s.SenderEmail }).IsUnique().HasDatabaseName("UQ_ES");
    }
}
