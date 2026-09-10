using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

/// <summary>
/// T-006b outbox table (TA-5.2 / TA-4.1.7). Known gap in TA-3.2 DDL — the table
/// row is sanctioned by `docs/Open-Decisions-and-Constants.md` Part 3 entry
/// "outbox table added per T-006/TA-5.2"; TA-3.2 will absorb the DDL row later.
/// </summary>
public class EventOutboxConfiguration : IEntityTypeConfiguration<EventOutbox>
{
    public void Configure(EntityTypeBuilder<EventOutbox> e)
    {
        e.ToTable("EventOutbox", "dbo");
        e.HasKey(k => k.Id).HasName("PK_EventOutbox");

        e.Property(b => b.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(b => b.EventId).HasColumnName("EventId").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(b => b.EventType).HasColumnName("EventType").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.HasIndex(i => i.EventId).IsUnique().HasDatabaseName("UQ_EventOutbox_EventId");
        e.Property(b => b.PayloadJson).HasColumnName("PayloadJson").HasColumnType("nvarchar(max)");
        e.Property(b => b.PartitionKey).HasColumnName("PartitionKey").HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        e.Property(b => b.CorrelationId).HasColumnName("CorrelationId").HasColumnType("varchar(64)").HasMaxLength(64);
        e.Property(b => b.SentAtUtc).HasColumnName("SentAtUtc").HasColumnType("datetime2");
    }
}