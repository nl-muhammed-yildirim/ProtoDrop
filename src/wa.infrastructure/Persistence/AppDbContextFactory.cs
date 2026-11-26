using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

/// <summary>
/// Runtime context factory (T-054-03): every store opens a fresh, short-lived context per call (CHORE-dbcontext-lifetime).
/// Distinct from the design-time <c>DesignTime.WaDbContextFactory</c> used by dotnet-ef.
/// </summary>
/// <param name="options">EF Core options shared by every context this factory creates.</param>
public sealed class AppDbContextFactory(DbContextOptions<WaDbContext> options) : IDbContextFactory<WaDbContext>
{
    private readonly DbContextOptions<WaDbContext> _options = options;

    /// <inheritdoc />
    public WaDbContext CreateDbContext() => new(_options);

    /// <inheritdoc />
    public Task<WaDbContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WaDbContext(_options));
}
