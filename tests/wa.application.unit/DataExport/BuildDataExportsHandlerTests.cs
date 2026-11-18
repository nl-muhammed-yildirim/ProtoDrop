using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.UseCases.DataExport;
using wa.domain;
using Export = wa.domain.DataExport;

namespace wa.application.unit.DataExport;

/// <summary>US-047-01 (FR-047-1/2/8, EC-047-2, EC-047-3): the f-data-export job handler over stubs.</summary>
public class BuildDataExportsHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 3, 0, DateTimeKind.Utc);

    private sealed class StubJobStore : IDataExportJobStore
    {
        public bool ClaimResult { get; set; } = true;
        public List<Export> Processing { get; } = [];
        public ExportSnapshot? Snapshot { get; set; }
        public List<PurgeableExport> Purgeable { get; } = [];
        public int SnapshotReads { get; private set; }
        public bool Completed { get; private set; }
        public List<(Guid Id, string Path)> Ready { get; } = [];
        public List<Guid> Failed { get; } = [];
        public List<Guid> Deleted { get; } = [];

        public Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct) => Task.FromResult(ClaimResult);
        public Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct) { Completed = true; return Task.CompletedTask; }
        public Task<IReadOnlyList<Export>> GetProcessingAsync(int batchSize, CancellationToken ct) => Task.FromResult<IReadOnlyList<Export>>(Processing.ToList());
        public Task<ExportSnapshot?> ReadSnapshotAsync(Guid userId, CancellationToken ct) { SnapshotReads++; return Task.FromResult(Snapshot); }
        public Task MarkReadyAsync(Guid id, string blobPath, DateTime readyAtUtc, CancellationToken ct) { Ready.Add((id, blobPath)); return Task.CompletedTask; }
        public Task MarkFailedAsync(Guid id, CancellationToken ct) { Failed.Add(id); return Task.CompletedTask; }
        public Task<IReadOnlyList<PurgeableExport>> GetPurgeableAsync(DateTime readyBeforeUtc, int batchSize, CancellationToken ct) => Task.FromResult<IReadOnlyList<PurgeableExport>>(Purgeable.ToList());
        public Task DeleteAsync(Guid id, CancellationToken ct) { Deleted.Add(id); return Task.CompletedTask; }
    }

    private sealed class CapturingBlobStore : IBlobStore
    {
        public bool ThrowOnUpload { get; set; }
        public List<string> Uploads { get; } = [];
        public List<string> Deletes { get; } = [];

        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default)
        {
            if (ThrowOnUpload) throw new IOException("blob down");
            Uploads.Add(path);
            return Task.CompletedTask;
        }
        public string? ThrowOnDeletePath { get; set; }
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (path == ThrowOnDeletePath) throw new IOException("blob delete down");
            Deletes.Add(path);
            return Task.CompletedTask;
        }
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Stream.Null);
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(0L);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static ExportSnapshot Snap() => new(new ExportProfile("a@x.com", "Ada", "system", "free", Now), []);

    private static Export Row() => new() { Id = Guid.NewGuid(), AppUserId = Guid.NewGuid(), State = DataExportState.Processing, CreatedAtUtc = Now };

    [Fact]
    public async Task ClaimLost_ReturnsZero_NothingRead()
    {
        var store = new StubJobStore { ClaimResult = false };
        store.Processing.Add(Row());
        var blobs = new CapturingBlobStore();
        var n = await new BuildDataExportsHandler(store, blobs, new CapturingLogger<BuildDataExportsHandler>()).RunAsync(Now, default);
        Assert.Equal(0, n);
        Assert.Equal(0, store.SnapshotReads);
        Assert.Empty(blobs.Uploads);
    }

    [Fact]
    public async Task ProcessingRow_UploadsAtExportPath_ThenMarksReady_ClaimCompleted()
    {
        var store = new StubJobStore { Snapshot = Snap() };
        var row = Row();
        store.Processing.Add(row);
        var blobs = new CapturingBlobStore();
        var n = await new BuildDataExportsHandler(store, blobs, new CapturingLogger<BuildDataExportsHandler>()).RunAsync(Now, default);
        var expected = BlobPaths.DataExportZip(row.AppUserId, row.Id);
        Assert.Equal(1, n);
        Assert.Equal([expected], blobs.Uploads);
        Assert.Equal([(row.Id, expected)], store.Ready);
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task UploadThrows_MarksFailed_LogsErrorWithExportId() // EC-047-2
    {
        var store = new StubJobStore { Snapshot = Snap() };
        var row = Row();
        store.Processing.Add(row);
        var log = new CapturingLogger<BuildDataExportsHandler>();
        var n = await new BuildDataExportsHandler(store, new CapturingBlobStore { ThrowOnUpload = true }, log).RunAsync(Now, default);
        Assert.Equal(0, n);
        Assert.Equal([row.Id], store.Failed);
        Assert.Empty(store.Ready);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Exception is IOException);
        Assert.True(log.Mentions(row.Id.ToString()));
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task UserGone_MarksFailed_NoUpload() // EC-047-3
    {
        var store = new StubJobStore { Snapshot = null };
        var row = Row();
        store.Processing.Add(row);
        var blobs = new CapturingBlobStore();
        await new BuildDataExportsHandler(store, blobs, new CapturingLogger<BuildDataExportsHandler>()).RunAsync(Now, default);
        Assert.Equal([row.Id], store.Failed);
        Assert.Empty(blobs.Uploads);
    }

    [Fact]
    public async Task PurgeableRow_DeletesBlobAndRow()
    {
        var store = new StubJobStore();
        var id = Guid.NewGuid();
        store.Purgeable.Add(new PurgeableExport(id, "exports/u/e.zip"));
        var blobs = new CapturingBlobStore();
        await new BuildDataExportsHandler(store, blobs, new CapturingLogger<BuildDataExportsHandler>()).RunAsync(Now, default);
        Assert.Equal(["exports/u/e.zip"], blobs.Deletes);
        Assert.Equal([id], store.Deleted);
    }

    [Fact]
    public async Task PurgeBlobDeleteThrows_RowKept_WarningLogged_OthersPurged() // US-047-04 D2
    {
        var store = new StubJobStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        store.Purgeable.Add(new PurgeableExport(first, "exports/u/first.zip"));
        store.Purgeable.Add(new PurgeableExport(second, "exports/u/second.zip"));
        var blobs = new CapturingBlobStore { ThrowOnDeletePath = "exports/u/first.zip" };
        var log = new CapturingLogger<BuildDataExportsHandler>();

        await new BuildDataExportsHandler(store, blobs, log).RunAsync(Now, default);

        Assert.Equal([second], store.Deleted);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.True(log.Mentions(first.ToString()));
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task PurgeBlobDeleteThrows_BuildLoopStillRuns() // EC-047-2
    {
        var store = new StubJobStore { Snapshot = Snap() };
        store.Purgeable.Add(new PurgeableExport(Guid.NewGuid(), "exports/u/bad.zip"));
        var row = Row();
        store.Processing.Add(row);
        var blobs = new CapturingBlobStore { ThrowOnDeletePath = "exports/u/bad.zip" };

        var n = await new BuildDataExportsHandler(store, blobs, new CapturingLogger<BuildDataExportsHandler>()).RunAsync(Now, default);

        Assert.Equal(1, n);
        Assert.Equal([row.Id], store.Ready.Select(r => r.Id));
        Assert.Empty(store.Deleted);
    }

    [Theory]
    [InlineData(12, 3, 12, 0)]
    [InlineData(12, 59, 12, 55)]
    public void TruncateTo5Min_FloorsMinute(int h, int m, int eh, int em)
    {
        var t = BuildDataExportsHandler.TruncateTo5Min(new DateTime(2026, 11, 18, h, m, 42, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 11, 18, eh, em, 0, DateTimeKind.Utc), t);
    }
}
