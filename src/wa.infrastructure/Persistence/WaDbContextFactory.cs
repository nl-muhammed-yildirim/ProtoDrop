using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

/// <summary>Minimal DbContext factory for the workers' SbEventPublisher (TA-0.2 rule 7: DI picks the adapter).</summary>
public sealed class WaDbContextFactory : IDbContextFactory<WaDbContext>
{
    private readonly DbContextOptions<WaDbContext> _options;

    public WaDbContextFactory(DbContextOptions<WaDbContext> options) => _options = options;

    public Task<WaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WaDbContext(_options));

    // Sync overload required by EF Core 10's IDbContextFactory<T>.
    public WaDbContext CreateDbContext() => new(_options);
}
