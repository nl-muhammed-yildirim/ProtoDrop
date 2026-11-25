using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

/// <summary>Minimal DbContext factory for the workers' SbEventPublisher (TA-0.2 rule 7: DI picks the adapter).</summary>
/// <param name="options">EF Core options shared by every context this factory creates.</param>
public sealed class WaDbContextFactory(DbContextOptions<WaDbContext> options) : IDbContextFactory<WaDbContext>
{
    private readonly DbContextOptions<WaDbContext> _options = options;

    /// <inheritdoc />
    public Task<WaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WaDbContext(_options));

    /// <summary>Synchronous overload that EF Core 10 requires on <see cref="IDbContextFactory{TContext}"/>.</summary>
    /// <returns>A new context over the shared options.</returns>
    public WaDbContext CreateDbContext() => new(_options);
}
