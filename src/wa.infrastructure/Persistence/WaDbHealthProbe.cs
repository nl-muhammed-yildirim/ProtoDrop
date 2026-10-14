using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using wa.application.Ports;

namespace wa.infrastructure.Persistence;

/// <summary>
/// Real database ping (no fakes): auto-creates the configured database from master when missing
/// (dev-only convenience — prod is Bicep-seeded), then runs SELECT 1 against it.
/// Health pings connectivity only — no schema check (EC-050-4).
/// </summary>
public sealed class WaDbHealthProbe : IDbHealthProbe
{
    private const int ConnectTimeoutSeconds = 5;

    private readonly string _waConnectionString;
    private readonly string _masterConnectionString;
    private readonly string _databaseName;

    public WaDbHealthProbe(IConfiguration configuration)
    {
        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("WaDb"))
        {
            ConnectTimeout = ConnectTimeoutSeconds,
        };
        _databaseName = builder.InitialCatalog;
        _waConnectionString = builder.ConnectionString;
        builder.InitialCatalog = "master";
        _masterConnectionString = builder.ConnectionString;
    }

    public async Task PingAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseAsync(cancellationToken);
        using var connection = new SqlConnection(_waConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{_databaseName}') IS NULL CREATE DATABASE [{_databaseName}];";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
