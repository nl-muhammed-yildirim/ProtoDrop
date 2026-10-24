using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.infrastructure.Blob;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-005-02 exit checks: f-delete-transfers on seeded expired-past-grace transfers → Status=4, FileItems/EmailRecipients gone,
/// BlobRef.RefCount decremented, PhysicallyDeletedAtUtc set when RefCount hits 0 (AC-005-1 second clause);
/// double-run idempotency (AC-005-2); shared blob survives (EC-005-4).
/// Fresh SQL 2022 via Testcontainers per TA-14.3. No Azurite needed — pure DB + outbox.
/// </summary>
public class DeleteTransfersJobIntegrationTests : IAsyncLifetime
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

    private DeleteExpiredTransfersHandler BuildHandler(WaDbContext db)
    {
        var store = new ExpiryDeletionStore(db);
        // Outbox-backed publisher (T-054): writes the TA-5.2 envelope into DomainEvent via its own context, then publishes to an unreachable broker — row stays unpublished but committed.
        var telemetry = new wa.application.Telemetry.WaTelemetryReporter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<wa.application.Telemetry.WaTelemetryReporter>.Instance,
            new byte[32]);
        // Stub blob store — no Azurite in this test; just verify it's called without error.
        var blobStore = new NoOpBlobStore();
        return new DeleteExpiredTransfersHandler(store, _publisher, blobStore, telemetry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteExpiredTransfersHandler>.Instance);
    }

    // ---- AC-005-1 second clause: expired transfer past grace → Status=4 + rows gone + event ----

    [Fact]
    public async Task SeededTransfer_ExpiredPastGrace_IsDeleted_WithEvent()
    {
        var transferId = Guid.NewGuid();
        var blobRefId = Guid.NewGuid();
        _db.Set<BlobRef>().Add(new BlobRef
        {
            Id = blobRefId,
            BlobPath = "transfers/00000000-0000-0000-0000-000000000001/blob1.dat",
            SizeBytes = 1024,
            RefCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobRefId,
            OriginalName = "test.pdf",
            SizeBytes = 1024,
            SortOrder = 0
        });
        _db.Set<EmailRecipient>().Add(new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            Address = "recipient@example.com"
        });
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "DELT01A",
            Status = 2, // Expired
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-7),
            ExpiredAtUtc = DateTime.UtcNow.AddDays(-4), // past grace (3 days)
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 1024,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, count);

        // Verify status flipped to Deleted.
        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var transfer = await fresh.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal((byte)4, transfer.Status); // Deleted
        Assert.NotNull(transfer.DeletedAtUtc);

        // FileItems and EmailRecipients are gone.
        Assert.Equal(0, await fresh.Set<FileItem>().CountAsync(f => f.TransferId == transferId));
        Assert.Equal(0, await fresh.Set<EmailRecipient>().CountAsync(e => e.TransferId == transferId));

        // BlobRef.RefCount decremented to 0 and PhysicallyDeletedAtUtc set.
        var blob = await fresh.Set<BlobRef>().FirstAsync(b => b.Id == blobRefId);
        Assert.Equal(0, blob.RefCount);
        Assert.NotNull(blob.PhysicallyDeletedAtUtc);

        // Verify event row in outbox with reason.
        var events = await fresh.Set<DomainEvent>().Where(e => e.EventType == "transfer.deleted").ToListAsync();
        Assert.Single(events);
        var payloadJson = events[0].EnvelopeJson;
        using var doc = JsonDocument.Parse(payloadJson);
        Assert.Equal("expiry-grace", doc.RootElement.GetProperty("payload").GetProperty("reason").GetString());
    }

    // ---- EC-005-4: shared blob (RefCount > 1) survives after one transfer deleted ----

    [Fact]
    public async Task SharedBlob_RefCountGreaterThanOne_Survives()
    {
        var transferId1 = Guid.NewGuid();
        var transferId2 = Guid.NewGuid();
        var blobRefId = Guid.NewGuid();
        _db.Set<BlobRef>().Add(new BlobRef
        {
            Id = blobRefId,
            BlobPath = "transfers/00000000-0000-0000-0000-000000000002/blob1.dat",
            SizeBytes = 2048,
            RefCount = 2, // shared by two transfers
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId1,
            BlobRefId = blobRefId,
            OriginalName = "shared.pdf",
            SizeBytes = 2048,
            SortOrder = 0
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId2,
            BlobRefId = blobRefId,
            OriginalName = "shared.pdf",
            SizeBytes = 2048,
            SortOrder = 0
        });
        // Only transfer1 is expired past grace.
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId1,
            LinkId = "DELT02A",
            Status = 2,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-7),
            ExpiredAtUtc = DateTime.UtcNow.AddDays(-4),
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 2048,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
        });
        // transfer2 is still active.
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId2,
            LinkId = "DELT02B",
            Status = 1, // Active — not touched by this job
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 2048,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, count); // only transfer1 deleted

        // BlobRef.RefCount decremented from 2 to 1 — NOT zero.
        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var blob = await fresh.Set<BlobRef>().FirstAsync(b => b.Id == blobRefId);
        Assert.Equal(1, blob.RefCount);
        Assert.Null(blob.PhysicallyDeletedAtUtc); // not eligible for physical deletion yet

        // transfer2's FileItem still exists.
        Assert.Equal(1, await fresh.Set<FileItem>().CountAsync(f => f.TransferId == transferId2));
    }

    // ---- AC-005-2: double run in same window → no duplicate events, second returns 0 ----

    [Fact]
    public async Task DoubleRun_SameWindow_NoDoubleDecrement()
    {
        var transferId = Guid.NewGuid();
        var blobRefId = Guid.NewGuid();
        _db.Set<BlobRef>().Add(new BlobRef
        {
            Id = blobRefId,
            BlobPath = "transfers/00000000-0000-0000-0000-000000000003/blob1.dat",
            SizeBytes = 512,
            RefCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobRefId,
            OriginalName = "doc.txt",
            SizeBytes = 512,
            SortOrder = 0
        });
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "DELT03A",
            Status = 2,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-7),
            ExpiredAtUtc = DateTime.UtcNow.AddDays(-4),
            MaxDownloads = 100,
            SenderName = "Test",
            TotalBytes = 512,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
        });
        await _db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var handler = BuildHandler(_db);

        // First run: deletes the transfer.
        var count1 = await handler.RunAsync(now, CancellationToken.None);
        Assert.Equal(1, count1);

        // Second run in the same 15-min window: JobRun claim already exists → returns 0.
        var count2 = await handler.RunAsync(now.AddSeconds(30), CancellationToken.None);
        Assert.Equal(0, count2);

        // Verify only one event row and RefCount is 0 (not -1 from double decrement).
        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var blob = await fresh.Set<BlobRef>().FirstAsync(b => b.Id == blobRefId);
        Assert.Equal(0, blob.RefCount);

        var events = await fresh.Set<DomainEvent>().Where(e => e.EventType == "transfer.deleted").ToListAsync();
        Assert.Single(events);
    }

    // ---- Status=3 (DownloadLimit) also picked up by this job ----

    [Fact]
    public async Task DownloadLimitTransfer_Alsodeleted()
    {
        var transferId = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "DELT04A",
            Status = 3, // DownloadLimit
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-7),
            ExpiredAtUtc = DateTime.UtcNow.AddDays(-5), // past grace
            MaxDownloads = 5,
            DownloadsCount = 5,
            SenderName = "Test",
            TotalBytes = 256,
            FileCount = 0, // no files — edge case: zero FileItems
            CreatedAtUtc = DateTime.UtcNow.AddDays(-14)
        });
        await _db.SaveChangesAsync();

        var handler = BuildHandler(_db);
        var count = await handler.RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, count);

        var fresh = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var transfer = await fresh.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        Assert.Equal((byte)4, transfer.Status); // Deleted
    }

    // ---- NoOp blob store for integration tests (no Azurite needed) ----

    private sealed class NoOpBlobStore : wa.application.Ports.IBlobStore
    {
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Stream.Null);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
