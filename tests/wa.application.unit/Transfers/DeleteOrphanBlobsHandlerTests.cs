using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.UseCases.Transfers;

namespace wa.application.unit.Transfers;

// T-005-03: f-delete-blobs handler — batch loop, JobRun claim (hourly), blob deletion + row removal.
public class DeleteOrphanBlobsHandlerTests
{
    private readonly StubBlobDeletionStore _store;
    private readonly StubBlobStore _blobStore;
    private readonly DeleteOrphanBlobsHandler _handler;

    public DeleteOrphanBlobsHandlerTests()
    {
        _store = new StubBlobDeletionStore();
        _blobStore = new StubBlobStore();
        _handler = new DeleteOrphanBlobsHandler(_store, _blobStore, NullLogger<DeleteOrphanBlobsHandler>.Instance);
    }

    [Fact]
    public async Task Run_HappyPath_ThreeBlobs_DeletesAll()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = new List<List<BlobDeletionBatchRow>>
        {
            new()
            {
                new BlobDeletionBatchRow(Guid.NewGuid(), "transfers/aaaa-bbbb/all.zip"),
                new BlobDeletionBatchRow(Guid.NewGuid(), "transfers/cccc-dddd/blob1.dat"),
                new BlobDeletionBatchRow(Guid.NewGuid(), "transfers/eeee-ffff/blob2.dat")
            }
        };

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal(3, _blobStore.DeletedPaths.Count);
    }

    [Fact]
    public async Task Run_ClaimLost_ReturnsZero_NoBlobsDeleted()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = false;

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(_blobStore.DeletedPaths);
    }

    [Fact]
    public async Task Run_EmptyBatch_ReturnsZero_CompletesClaim()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = [[]]; // first batch empty → loop exits immediately

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.True(_store.ClaimCompleted);
    }

    [Fact]
    public async Task Run_UnexpectedPath_Skipped_BlobDelete_RowStillRemoved()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        // One valid path, one unexpected (staging/).
        var validId = Guid.NewGuid();
        var unexpectedId = Guid.NewGuid();
        _store.Batches = new List<List<BlobDeletionBatchRow>>
        {
            new()
            {
                new BlobDeletionBatchRow(validId, "transfers/aaaa-bbbb/all.zip"),
                new BlobDeletionBatchRow(unexpectedId, "staging/temp/blob.dat")
            }
        };

        var count = await _handler.RunAsync(now, CancellationToken.None);

        // Both counted in total (the handler counts batch.Count).
        Assert.Equal(2, count);
        // Only the valid one was actually blob-deleted.
        Assert.Single(_blobStore.DeletedPaths);
        // Both rows are deleted — the unexpected path row is removed to prevent infinite re-scan (FR-005-7).
        Assert.Equal(2, _store.DeletedRowIds.Count);
    }

    [Fact]
    public async Task Run_BlobNotFound_Swallowed_RowStillDeleted()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        var blobId = Guid.NewGuid();
        _store.Batches = new List<List<BlobDeletionBatchRow>>
        {
            new() { new BlobDeletionBatchRow(blobId, "transfers/aaaa-bbbb/all.zip") }
        };
        // Blob deletion throws — should be swallowed.
        _blobStore.ShouldThrow = true;

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(1, count);
        // Row still deleted.
        Assert.Single(_store.DeletedRowIds);
    }

    [Fact]
    public void TruncateToHour_VariousTimes_Correct()
    {
        Assert.Equal(new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc), DeleteOrphanBlobsHandler.TruncateToHour(new DateTime(2025, 6, 15, 10, 3, 45, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc), DeleteOrphanBlobsHandler.TruncateToHour(new DateTime(2025, 6, 15, 10, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 11, 0, 0, DateTimeKind.Utc), DeleteOrphanBlobsHandler.TruncateToHour(new DateTime(2025, 6, 15, 11, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc), DeleteOrphanBlobsHandler.TruncateToHour(new DateTime(2025, 6, 15, 0, 14, 59, DateTimeKind.Utc)));
    }

    // ---- Stubs ----

    private sealed class StubBlobDeletionStore : IBlobDeletionStore
    {
        public bool ClaimResult { get; set; } = true;
        public List<List<BlobDeletionBatchRow>> Batches { get; set; } = [];
        public List<Guid> DeletedRowIds { get; } = [];
        public bool ClaimCompleted { get; private set; }

        private int _batchIndex;

        public Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct) =>
            Task.FromResult(ClaimResult);

        public Task<IReadOnlyList<BlobDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            if (_batchIndex < Batches.Count)
                return Task.FromResult<IReadOnlyList<BlobDeletionBatchRow>>(Batches[_batchIndex++]);
            return Task.FromResult<IReadOnlyList<BlobDeletionBatchRow>>([]);
        }

        public Task DeleteRowAsync(Guid blobRefId, CancellationToken ct)
        {
            DeletedRowIds.Add(blobRefId);
            return Task.CompletedTask;
        }

        public Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
        {
            ClaimCompleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubBlobStore : IBlobStore
    {
        public List<string> DeletedPaths { get; } = [];
        public bool ShouldThrow { get; set; }

        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Stream.Null);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow) return Task.FromException(new InvalidOperationException("BlobNotFound"));
            DeletedPaths.Add(path);
            return Task.CompletedTask;
        }
    }
}
