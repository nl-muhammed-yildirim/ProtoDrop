using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace wa.infrastructure.Persistence.DesignTime;

/// <summary>
/// Migrations tooling entry point (T-052-03): dotnet ef resolves this instead of the host, so the dev connection string
/// from launchSettings.json is duplicated here — no app services are required to design-time build the model.
/// </summary>
public sealed class WaDbContextFactory : IDesignTimeDbContextFactory<WaDbContext>
{
    public WaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer("Server=localhost,1433;User Id=sa;Password=YourStrong!Passw0rd;Database=wa;TrustServerCertificate=True")
            .Options;

        return new WaDbContext(options);
    }
}
