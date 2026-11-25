using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.UseCases.DataExport;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-047-01 (AC-047-1, AC-047-3, EC-047-1/2/3): f-data-export against real SQL with an in-memory blob store.
/// Fresh SQL 2022 per fact; no Azurite needed.
/// </summary>
public class DataExportJobIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private Guid _userId;
    private Guid _planId;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(_options);
        await _db.Database.MigrateAsync();

        _planId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
        _userId = await AddUserAsync("owner@x.com");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private async Task<Guid> AddUserAsync(string email)
    {
        var id = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser { Id = id, Email = email, DisplayName = "U", PlanId = _planId, Theme = "system", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddTransferAsync(Guid? owner, string link, string[] fileNames, string[] recipients, byte status = 1)
    {
        var id = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = link,
            OwnerAppUserId = owner,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            MaxDownloads = 10,
            SenderName = "S",
            TotalBytes = 100,
            FileCount = fileNames.Length,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        for (var i = 0; i < fileNames.Length; i++)
        {
            var blobRef = new BlobRef { Id = Guid.NewGuid(), BlobPath = BlobPaths.TransferFile(id, Guid.NewGuid()), RefCount = 1, SizeBytes = 10 };
            _db.Set<BlobRef>().Add(blobRef);
            _db.Set<FileItem>().Add(new FileItem { Id = Guid.NewGuid(), TransferId = id, BlobRefId = blobRef.Id, OriginalName = fileNames[i], SizeBytes = 10 + i, SortOrder = i });
        }
        foreach (var r in recipients)
            _db.Set<EmailRecipient>().Add(new EmailRecipient { Id = Guid.NewGuid(), TransferId = id, Address = r });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<DataExport> AddExportAsync(Guid userId, byte state = DataExportState.Processing, DateTime? readyAt = null, string? blobPath = null)
    {
        var row = new DataExport { Id = Guid.NewGuid(), AppUserId = userId, State = state, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1), ReadyAtUtc = readyAt, BlobPath = blobPath };
        _db.Set<DataExport>().Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private DataExportJobStore Store() => new(new WaDbContextFactory(_options));

    private BuildDataExportsHandler Handler(MemoryBlobStore blobs) =>
        new(Store(), blobs, Microsoft.Extensions.Logging.Abstractions.NullLogger<BuildDataExportsHandler>.Instance);

    private async Task<DataExport> ReloadAsync(Guid id)
    {
        await using var fresh = new WaDbContext(_options);
        return await fresh.Set<DataExport>().AsNoTracking().FirstAsync(e => e.Id == id);
    }

    /// <summary>The real delete path both DELETE /auth/me and admin force-delete use (soft delete + Erasure marker).</summary>
    private async Task DeleteUserAsync(Guid id)
    {
        await using var ctx = new WaDbContext(_options);
        Assert.True(await new AppUserStore(ctx).SoftDeleteWithReHomeAsync(id, DateTime.UtcNow, CancellationToken.None));
    }

    private async Task<bool> RowExistsAsync(Guid id)
    {
        await using var fresh = new WaDbContext(_options);
        return await fresh.Set<DataExport>().AnyAsync(e => e.Id == id);
    }

    private static Dictionary<string, JsonElement> ReadZip(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            return JsonDocument.Parse(s).RootElement.Clone();
        });
    }

    [Fact]
    public async Task FiveOwnedTransfers_ZipListsAll_WithFilesSizesRecipients() // AC-047-1
    {
        for (var i = 0; i < 5; i++)
            await AddTransferAsync(_userId, $"OWNED00{i}", [$"a{i}.pdf", $"b{i}.png"], [$"r{i}@x.com"]);
        var export = await AddExportAsync(_userId);
        var blobs = new MemoryBlobStore();

        var built = await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, built);
        var row = await ReloadAsync(export.Id);
        Assert.Equal(DataExportState.Ready, row.State);
        Assert.Equal(BlobPaths.DataExportZip(_userId, export.Id), row.BlobPath);
        Assert.NotNull(row.ReadyAtUtc);

        var parts = ReadZip(blobs.Blobs[row.BlobPath!]);
        Assert.Equal("owner@x.com", parts["profile.json"].GetProperty("email").GetString());
        Assert.Equal("free", parts["profile.json"].GetProperty("plan").GetString());
        var transfers = parts["transfers.json"];
        Assert.Equal(5, transfers.GetArrayLength());
        var first = transfers[0];
        Assert.Equal(2, first.GetProperty("files").GetArrayLength());
        Assert.Equal("a0.pdf", first.GetProperty("files")[0].GetProperty("name").GetString());
        Assert.Equal(10, first.GetProperty("files")[0].GetProperty("sizeBytes").GetInt64());
        Assert.Equal("r0@x.com", first.GetProperty("recipients")[0].GetString());
    }

    [Fact]
    public async Task ReceivedTransfer_NotExported() // AC-047-3
    {
        var friend = await AddUserAsync("friend@x.com");
        await AddTransferAsync(friend, "FRIEND01", ["secret.pdf"], ["owner@x.com"]); // my email is only a recipient
        await AddTransferAsync(_userId, "MINE0001", ["mine.pdf"], []);
        var export = await AddExportAsync(_userId);
        var blobs = new MemoryBlobStore();

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        var transfers = ReadZip(blobs.Blobs[(await ReloadAsync(export.Id)).BlobPath!])["transfers.json"];
        Assert.Equal(1, transfers.GetArrayLength());
        Assert.Equal("mine.pdf", transfers[0].GetProperty("files")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ZeroTransfers_EmptyArray() // EC-047-1
    {
        var export = await AddExportAsync(_userId);
        var blobs = new MemoryBlobStore();

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        var parts = ReadZip(blobs.Blobs[(await ReloadAsync(export.Id)).BlobPath!]);
        Assert.Equal(0, parts["transfers.json"].GetArrayLength());
        Assert.Equal("owner@x.com", parts["profile.json"].GetProperty("email").GetString());
    }

    [Fact]
    public async Task BlobFails_RowFailed() // EC-047-2
    {
        var export = await AddExportAsync(_userId);

        await Handler(new MemoryBlobStore { FailUpload = true }).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(DataExportState.Failed, (await ReloadAsync(export.Id)).State);
    }

    [Fact]
    public async Task SoftDeletedUser_RowFailed_NoUpload() // EC-047-3
    {
        var export = await AddExportAsync(_userId);
        var user = await _db.Set<AppUser>().FirstAsync(u => u.Id == _userId);
        user.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var blobs = new MemoryBlobStore();

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(DataExportState.Failed, (await ReloadAsync(export.Id)).State);
        Assert.Empty(blobs.Blobs);
    }

    [Fact]
    public async Task ReadyOlderThan7Days_PurgedBlobAndRow()
    {
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var old = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow.AddDays(-8), path);
        var blobs = new MemoryBlobStore();
        blobs.Blobs[path] = [1, 2, 3];

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.DoesNotContain(path, blobs.Blobs.Keys);
        await using var fresh = new WaDbContext(_options);
        Assert.False(await fresh.Set<DataExport>().AnyAsync(e => e.Id == old.Id));
    }

    [Fact]
    public async Task ReadyPurged_OlderFailedRow_DoesNotSurfaceAsLatest() // review m4
    {
        var failed = await AddExportAsync(_userId, DataExportState.Failed);
        failed.CreatedAtUtc = DateTime.UtcNow.AddDays(-10);
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var ready = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow.AddDays(-8), path);
        ready.CreatedAtUtc = DateTime.UtcNow.AddDays(-9);
        await _db.SaveChangesAsync();
        var blobs = new MemoryBlobStore();
        blobs.Blobs[path] = [1];

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        var latest = await new DataExportStore(new WaDbContextFactory(_options)).GetLatestAsync(_userId, CancellationToken.None);
        Assert.Null(latest);
    }

    [Fact]
    public async Task DeletedUser_FreshReadyExport_BlobAndRowPurgedNextRun() // US-047-04 D1
    {
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var ready = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow.AddHours(-1), path);
        var blobs = new MemoryBlobStore();
        blobs.Blobs[path] = [1, 2, 3];
        await DeleteUserAsync(_userId);

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.DoesNotContain(path, blobs.Blobs.Keys);
        Assert.False(await RowExistsAsync(ready.Id));
    }

    [Fact]
    public async Task LiveUser_FreshReadyExport_NotPurged()
    {
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var ready = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow.AddHours(-1), path);
        var blobs = new MemoryBlobStore();
        blobs.Blobs[path] = [1, 2, 3];

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Contains(path, blobs.Blobs.Keys);
        Assert.True(await RowExistsAsync(ready.Id));
    }

    [Fact]
    public async Task DeletedUser_ProcessingExport_FailsCleanly_NoZip_ErasureOnce() // EC-047-3
    {
        var export = await AddExportAsync(_userId);
        await DeleteUserAsync(_userId);
        var blobs = new MemoryBlobStore();
        var now = DateTime.UtcNow;

        await Handler(blobs).RunAsync(now, CancellationToken.None);
        await Handler(blobs).RunAsync(now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(DataExportState.Failed, (await ReloadAsync(export.Id)).State);
        Assert.Empty(blobs.Blobs);
        await using var fresh = new WaDbContext(_options);
        Assert.Equal(1, await fresh.Set<Erasure>().CountAsync(e => e.AppUserId == _userId));
    }

    [Fact]
    public async Task ReadyLandedAfterDelete_PurgedNextRun() // snapshot-before-delete race
    {
        await DeleteUserAsync(_userId);
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var ready = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow, path);
        var blobs = new MemoryBlobStore();
        blobs.Blobs[path] = [9];

        await Handler(blobs).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.DoesNotContain(path, blobs.Blobs.Keys);
        Assert.False(await RowExistsAsync(ready.Id));
    }

    [Fact]
    public async Task MarkReady_OnPurgedOrFailedRow_NoResurrection()
    {
        var purgedId = Guid.NewGuid();
        await Store().MarkReadyAsync(purgedId, "exports/x/y.zip", DateTime.UtcNow, CancellationToken.None);
        Assert.False(await RowExistsAsync(purgedId));

        var failed = await AddExportAsync(_userId, DataExportState.Failed);
        await Store().MarkReadyAsync(failed.Id, "exports/x/z.zip", DateTime.UtcNow, CancellationToken.None);

        var row = await ReloadAsync(failed.Id);
        Assert.Equal(DataExportState.Failed, row.State);
        Assert.Null(row.BlobPath);
    }

    [Fact]
    public async Task BlobDeleteFails_RowKept_PurgedOnRetry() // EC-047-2
    {
        var path = BlobPaths.DataExportZip(_userId, Guid.NewGuid());
        var ready = await AddExportAsync(_userId, DataExportState.Ready, DateTime.UtcNow.AddHours(-1), path);
        var blobs = new MemoryBlobStore { FailDelete = true };
        blobs.Blobs[path] = [1];
        await DeleteUserAsync(_userId);
        var now = DateTime.UtcNow;

        await Handler(blobs).RunAsync(now, CancellationToken.None);
        Assert.True(await RowExistsAsync(ready.Id));
        Assert.Contains(path, blobs.Blobs.Keys);

        blobs.FailDelete = false;
        await Handler(blobs).RunAsync(now.AddMinutes(5), CancellationToken.None);
        Assert.False(await RowExistsAsync(ready.Id));
        Assert.DoesNotContain(path, blobs.Blobs.Keys);
    }

    [Fact]
    public async Task SecondRunSameWindow_ClaimLost()
    {
        var blobs = new MemoryBlobStore();
        var now = DateTime.UtcNow;
        await AddExportAsync(_userId);

        var first = await Handler(blobs).RunAsync(now, CancellationToken.None);
        await AddExportAsync(_userId);
        var second = await Handler(blobs).RunAsync(now, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task InFlightIndex_RejectsSecondProcessingRow()
    {
        var store = new DataExportStore(new WaDbContextFactory(_options));
        var a = new DataExport { Id = Guid.NewGuid(), AppUserId = _userId, State = DataExportState.Processing, CreatedAtUtc = DateTime.UtcNow };
        var b = new DataExport { Id = Guid.NewGuid(), AppUserId = _userId, State = DataExportState.Processing, CreatedAtUtc = DateTime.UtcNow };

        Assert.True(await store.TryCreateAsync(a, CancellationToken.None));
        Assert.False(await store.TryCreateAsync(b, CancellationToken.None));
    }

    private sealed class MemoryBlobStore : IBlobStore
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];
        public bool FailUpload { get; set; }
        public bool FailDelete { get; set; }

        public async Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default)
        {
            if (FailUpload) throw new IOException("blob down");
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            Blobs[path] = ms.ToArray();
        }
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (FailDelete) throw new IOException("blob delete down");
            Blobs.Remove(path);
            return Task.CompletedTask;
        }
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Blobs[path]));
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult((long)Blobs[path].Length);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Blobs.ContainsKey(path));
    }
}
