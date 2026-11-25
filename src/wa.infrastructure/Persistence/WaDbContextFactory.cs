using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

/// <summary>Minimal DbContext factory for the workers' SbEventPublisher (TA-0.2 rule 7: DI picks the adapter).</summary>
public sealed class WaDbContextFactory : IDbContextFactory<WaDbContext>
{
    private readonly DbContextOptions<WaDbContext> _options;

    /// <summary>Initializes a new instance of the <see cref="WaDbContextFactory"/> class.</summary>
    /// <param name="options">The options.</param>
    public WaDbContextFactory(DbContextOptions<WaDbContext> options) => _options = options;

    /// <inheritdoc />
    public Task<WaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new WaDbContext(_options));

    /// <summary>Sync overload required by EF Core 10's IDbContextFactory&lt;T&gt;.</summary>
    /// <returns>The wa database context.</returns>
    public WaDbContext CreateDbContext() => new(_options);
}
