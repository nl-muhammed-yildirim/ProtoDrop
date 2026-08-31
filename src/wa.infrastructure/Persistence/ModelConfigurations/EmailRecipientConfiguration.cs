using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class EmailRecipientConfiguration : IEntityTypeConfiguration<EmailRecipient>
{
    public void Configure(EntityTypeBuilder<EmailRecipient> e)
    {
        e.ToTable("EmailRecipient", "dbo");
        // DDL: CONSTRAINT PK_ER PRIMARY KEY.
        // DDL: CONSTRAINT PK_ER PRIMARY KEY. PK name goes through RelationalKeyBuilderExtensions.HasName
        // on KeyBuilder; no HasConstraintName overload exists for keys on the relational builder surface.
        e.HasKey(k => k.Id).HasName("PK_ER");

        e.Property(r => r.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(r => r.TransferId).HasColumnName("TransferId").HasColumnType("uniqueidentifier").IsRequired();
        e.Property(r => r.Address).HasColumnName("Address").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired();
        e.Property(r => r.NotifiedAtUtc).HasColumnName("NotifiedAtUtc").HasColumnType("datetime2");

        // DDL: CONSTRAINT FK_ER_T REFERENCES dbo.Transfer(Id).
        // DDL: no ON DELETE clause -> NO ACTION; EF defaults required->CASCADE.
        e.HasOne<Transfer>().WithMany().HasForeignKey(r => r.TransferId)
            .HasConstraintName("FK_ER_T")
            .OnDelete(DeleteBehavior.NoAction);

        e.HasIndex(i => new { i.TransferId, i.Address }).IsUnique().HasDatabaseName("UQ_ER");
    }
}
