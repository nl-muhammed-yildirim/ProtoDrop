using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> e)
    {
        e.ToTable("JobRun", "dbo");
        e.HasKey(k => new { k.JobKey, k.RunAtUtc });

        e.Property(j => j.JobKey).HasColumnName("JobKey").HasColumnType("varchar(64)").HasMaxLength(64).IsRequired();
        e.Property(j => j.RunAtUtc).HasColumnName("RunAtUtc").HasColumnType("datetime2");
        e.Property(j => j.LockUntilUtc).HasColumnName("LockUntilUtc").HasColumnType("datetime2");
        e.Property(j => j.FinishedAtUtc).HasColumnName("FinishedAtUtc").HasColumnType("datetime2");
    }
}
