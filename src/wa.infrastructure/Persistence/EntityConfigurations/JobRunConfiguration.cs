using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.JobRun — composite PK, no single-column key property.</summary>
public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> b)
    {
        b.ToTable("JobRun");
        // CONSTRAINT PK_JobRun PRIMARY KEY (JobKey, RunAtUtc) — timer de-dup (TA-6.1).
        b.HasKey(j => new { j.JobKey, j.RunAtUtc }).HasName("PK_JobRun");

        b.Property(j => j.JobKey).HasColumnType("VARCHAR(64)").IsRequired();
    }
}
