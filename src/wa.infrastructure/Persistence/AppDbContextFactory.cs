using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

/// <summary>
/// Runtime context factory (T-054-03): adapters own their contexts, no request scope in the current API surface.
/// Distinct from the design-time <c>DesignTime.WaDbContextFactory</c> used by dotnet-ef.
/// </summary>
public sealed class AppDbContextFactory : IDbContextFactory<WaDbContext>
{
    private readonly DbContextOptions<WaDbContext> _options;

    public AppDbContextFactory(DbContextOptions<WaDbContext> options) => _options = options;

    public WaDbContext CreateDbContext() => new(_options);

    public Task<WaDbContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WaDbContext(_options));
}
