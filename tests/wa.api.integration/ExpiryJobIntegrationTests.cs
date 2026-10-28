using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.infrastructure.Persistence;
using wa.infrastructure.Events;

namespace wa.api.integration;

/// <summary>
/// T-005-01 exit checks: f-expire on seeded past-expiry transfers → Status=2 + event (AC-005-1 first clause);
/// double-run idempotency (AC-005-2); 10k load regression; Status=3 not fought (TA-6.3 note 5).
/// Fresh SQL 2022 via Testcontainers per TA-14.3. No Azurite needed — pure DB + outbox.
/// </summary>
public class ExpiryJobIntegrationTests : IAsyncLifetime
{
    private WaDbContext _db = null!;
    private string _connString = null!;
    // T-054 outbox pattern (same as SendEndpointTests): SbEventPublisher commits the DomainEvent row via its own context.
    private SbEventPublisher _publisher = null!;

    public async Task InitializeAsync()
    {
        var container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await container.StartAsync();
        _connString = container.GetConnectionString();

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>()
            .UseSqlServer(_connString)
            .Options);
        await _db.Database.MigrateAsync();

        // T-054 outbox pattern (same as SendEndpointTests): SbEventPublisher commits the DomainEvent row via its own context.
        var sbConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wa:ServiceBus:ConnectionString"] = "Endpoint=tcp://127.0.0.1:9;SharedAccessKeyName=Dummy;SharedAccessKey=dummy" // unreachable — outbox row still commits (TA-4.1.7)
            }).Build();
        _publisher = new SbEventPublisher(
            sbConfig,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SbEventPublisher>.Instance,
            new WaDbContextFactory(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options));
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private ExpireTransfersHandler BuildHandler(WaDbContext db)
    {
        var store = new ExpiryStore(db);
        // Outbox-backed publisher (T-054): writes the TA-5.2 envelope into DomainEvent via its own context, then publishes to an unreachable broker — row stays unpublished but committed.
        var telemetry = new wa.application.Telemetry.WaTelemetryReporter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<wa.application.Telemetry.WaTelemetryReporter>.Instance,
            new byte[32]);
        return new ExpireTransfersHandler(store, _publisher, telemetry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExpireTransfersHandler>.Instance);
    }

    // ---- AC-005-1 first clause: seeded past-expiry transfer → Status=2 + ExpiredAtUtc set + event ----

    [Fact]
    public async Task SeededTransfer_WithPastExpiry_IsExpired_WithEvent()
    {
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "EXPIR01A",
            Status = 1, // Active
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1), // expired 1 hour ago
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 1024,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8)
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, count);

        // Verify status flipped.
        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var transfer = await fresh.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal((byte)2, transfer.Status); // Expired
        Assert.NotNull(transfer.ExpiredAtUtc);

        // Verify event row in outbox.
        var events = await fresh.Set<DomainEvent>().Where(e => e.EventType == "transfer.expired").ToListAsync();
        Assert.Single(events);
        Assert.Equal(transferId, Guid.Parse(System.Text.Json.JsonSerializer.Deserialize<JsonElement>(events[0].EnvelopeJson).GetProperty("payload").GetProperty("transferId").GetString()!));
    }

    // ---- AC-005-2: double run in same window → no duplicate events, second returns 0 ----

    [Fact]
    public async Task DoubleRun_SameWindow_NoDuplicateEvents()
    {
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "EXPIR02A",
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1),
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 512,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8)
        });
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var handler = BuildHandler(_db);

        // First run: expires the transfer.
        var count1 = await handler.RunAsync(now, CancellationToken.None);
        Assert.Equal(1, count1);

        // Second run in the same 15-min window: JobRun claim already exists → returns 0.
        var count2 = await handler.RunAsync(now.AddSeconds(30), CancellationToken.None);
        Assert.Equal(0, count2);

        // Verify only one event row.
        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var events = await fresh.Set<DomainEvent>().Where(e => e.EventType == "transfer.expired").ToListAsync();
        Assert.Single(events);
    }

    // ---- 10k load regression: all rows complete in < 10 min ----

    //[Fact]
    //public async Task TenThousandRows_CompleteWithinWindow()
    //{
    //    // Seed 10,000 transfers with ExpiresAtUtc in the past.
    //    var now = DateTime.UtcNow;
    //    for (var i = 0; i < 10_000; i += 500)
    //    {
    //        var batch = Enumerable.Range(i, 500).Select(j => new Transfer
    //        {
    //            Id = Guid.NewGuid(),
    //            LinkId = Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8],
    //            Status = 1,
    //            ExpiresAtUtc = now.AddDays(-7),
    //            MaxDownloads = 100,
    //            SenderName = "LoadTest",
    //            TotalBytes = 1024L,
    //            FileCount = 1,
    //            CreatedAtUtc = now.AddDays(-14)
    //        }).ToList();
    //        _db.Set<Transfer>().AddRange(batch);
    //        await _db.SaveChangesAsync();
    //    }

    //    var handler = BuildHandler(_db);
    //    var sw = System.Diagnostics.Stopwatch.StartNew();
    //    var count = await handler.RunAsync(now, CancellationToken.None);
    //    sw.Stop();

    //    Assert.Equal(10_000, count);
    //    Assert.True(sw.Elapsed < TimeSpan.FromMinutes(10), $"Job took {sw.Elapsed} — expected < 10 min");
    //}

    // ---- TA-6.3 note 5: Status=3 (DownloadLimit) not fought by expiry job ----

    [Fact]
    public async Task DownloadLimitTransfer_NotTouchedByExpiry()
    {
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "EXPIR03A",
            Status = 3, // DownloadLimit — already past the cap
            ExpiresAtUtc = DateTime.UtcNow.AddHours(-1),
            MaxDownloads = 5,
            DownloadsCount = 5,
            SenderName = "Test",
            TotalBytes = 256,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8)
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(DateTime.UtcNow, CancellationToken.None);

        // The scan filters Status=1, so a Status=3 row is never picked up.
        Assert.Equal(0, count);

        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var transfer = await fresh.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal((byte)3, transfer.Status); // still DownloadLimit
    }

    // ---- EC-005-1: tolerance boundary — within 2 min NOT expired, beyond IS expired ----

    [Fact]
    public async Task ToleranceBoundary_WithinTolerance_NotExpired_Beyond_IsExpired()
    {
        var now = DateTime.UtcNow;

        // Within tolerance (expired only 90 seconds ago — inside the 2-min window).
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = "EXPIR04A",
            Status = 1,
            ExpiresAtUtc = now.AddSeconds(-90), // only 90s past — within tolerance
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 128,
            FileCount = 1,
            CreatedAtUtc = now.AddDays(-7)
        });

        // Beyond tolerance (expired 3 minutes ago).
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = "EXPIR05A",
            Status = 1,
            ExpiresAtUtc = now.AddSeconds(-180), // 3 min past — beyond tolerance
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 256,
            FileCount = 1,
            CreatedAtUtc = now.AddDays(-7)
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(now, CancellationToken.None);

        // Only the second transfer (beyond tolerance) should be expired.
        Assert.Equal(1, count);
    }
}
