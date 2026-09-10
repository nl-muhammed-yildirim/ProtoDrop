using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace wa.infrastructure.Persistence;

/// <summary>
/// Design-time factory so the `dotnet ef` CLI can build <see cref="WaDbContext"/>
/// without a running web host. The connection string is only used to pick the
/// provider; the actual SQL is emitted from the model snapshot.
/// </summary>
public class WaDbContextFactory : IDesignTimeDbContextFactory<WaDbContext>
{
    public WaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(
                "Server=127.0.0.1;User Id=sa;Password=YourStrong!Passw0rd;Database=wa;TrustServerCertificate=True");

        return new WaDbContext(optionsBuilder.Options);
    }
}