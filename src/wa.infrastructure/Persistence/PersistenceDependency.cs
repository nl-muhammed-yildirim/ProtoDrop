using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace wa.infrastructure.Persistence;

/// <summary>
/// T-006b — <see cref="WaDbContext"/> registration (EF, <c>ConnectionStrings:WaDb</c>).
/// </summary>
public static class PersistenceDependency
{
    /// <summary>
    /// Register <see cref="WaDbContext"/> (EF, <c>ConnectionStrings:WaDb</c>).
    /// </summary>
    public static IServiceCollection AddWaDbContext(
        this IServiceCollection services, IConfiguration configuration)
    {
        var conn = configuration.GetConnectionString("WaDb");

        services.AddDbContext<WaDbContext>(
            options =>
            {
                if (!string.IsNullOrWhiteSpace(conn))
                {
                    options.UseSqlServer(conn);
                }
            });

        return services;
    }
}