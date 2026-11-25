using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace wa.infrastructure.Persistence;

/// <summary>
/// EF model for the full TA-3.2 schema (T-052-02). One <see cref="IEntityTypeConfiguration{TEntity}"/> per entity,
/// discovered from this assembly — nothing persistence-related lives outside this folder (TA-2.3/ADR-015).
/// </summary>
public sealed class WaDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="WaDbContext"/> class.</summary>
    /// <param name="options">EF Core options (provider and connection) for this context.</param>
    public WaDbContext(DbContextOptions<WaDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        // TA-3.2 defines no FK-side indexes for PlanId/BlobRefId/AppUserId/SupersededBy — strip the convention that
        // auto-creates them (EF Core has no per-index suppression API). Removing it drops exactly those 4 unnamed
        // indexes and nothing else (verified by A/B build against a live DB before this migration was locked in).
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 16 configurations in EntityConfigurations/ (one per entity); the model build below proves discovery.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaDbContext).Assembly);
    }
}
