using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.EmailRecipient (re-send idempotency per address).</summary>
public sealed class EmailRecipientConfiguration : IEntityTypeConfiguration<EmailRecipient>
{
    public void Configure(EntityTypeBuilder<EmailRecipient> b)
    {
        b.ToTable("EmailRecipient");
        // Non-standard PK constraint name in DDL: CONSTRAINT PK_ER.
        b.HasKey(r => r.Id).HasName("PK_ER");

        b.Property(r => r.Address).HasColumnType("VARCHAR(320)").IsRequired();

        // T-006-04: Preferred language for email rendering (nullable — null/empty → English fallback).
        b.Property(r => r.PreferredLanguage).HasColumnType("VARCHAR(8)");

        // FK_ER_T -> dbo.Transfer(Id); DDL has no ON DELETE clause = SQL Server default NO ACTION (EC-052-1).
        b.HasOne<Transfer>()
            .WithMany()
            .HasForeignKey(r => r.TransferId)
            .HasConstraintName("FK_ER_T")
            .OnDelete(DeleteBehavior.NoAction);

        // Re-send idempotency: one notification row per address per transfer.
        b.HasIndex(r => new { r.TransferId, r.Address }).IsUnique().HasDatabaseName("UQ_ER");
    }
}
