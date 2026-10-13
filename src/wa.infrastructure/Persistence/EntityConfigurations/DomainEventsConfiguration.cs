using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>T-054-02: dbo.DomainEvents — outbox row for published domain events (new migration, golden rule 4).</summary>
public sealed class DomainEventsConfiguration : IEntityTypeConfiguration<DomainEvent>
{
    public void Configure(EntityTypeBuilder<DomainEvent> b)
    {
        b.ToTable("DomainEvents");

        // The envelope's EventId — Service Bus MessageId; broker-level dedup identity (TA-5.1). PK, unique.
        b.HasKey(e => e.EventId).HasName("PK_DomainEvents");
        b.Property(e => e.EventId).HasColumnType("uniqueidentifier").IsRequired();

        b.Property(e => e.EventType).HasColumnType("VARCHAR(64)").IsRequired();
        b.Property(e => e.EnvelopeJson).HasColumnType("NVARCHAR(MAX)").IsRequired();
        b.Property(e => e.CorrelationId).HasColumnType("uniqueidentifier");
        b.Property(e => e.PartitionKey).HasColumnType("VARCHAR(320)");
        b.Property(e => e.IsPublished).HasColumnType("BIT").IsRequired();
    }
}
