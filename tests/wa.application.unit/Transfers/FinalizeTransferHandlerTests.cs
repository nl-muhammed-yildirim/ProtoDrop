using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-001-10: the handler is the only business logic behind POST /api/v1/transfers/draft/{draftId}/finalize (TA-4.2a).
// Idempotent finalize (EC-002-1), server-side blob copy staging→transfers, create Transfer(Draft)+FileItems+BlobRefs in one transaction, emit upload_completed.
public class FinalizeTransferHandlerTests
{
    private readonly StubTransferStore _store;
    private readonly StubBlobStore _blobStore;
    private readonly RecordingLogger _logger;
    private readonly FinalizeTransferHandler _handler;

    public FinalizeTransferHandlerTests()
    {
        _store = new StubTransferStore();
        _blobStore = new StubBlobStore();
        _logger = new RecordingLogger();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Url:Public"] = "http://localhost:5173" })
            .Build();

        var telemetry = new WaTelemetryReporter(_logger, Encoding.UTF8.GetBytes("test-key"));
        _handler = new FinalizeTransferHandler(
            _store,
            _blobStore,
            new StubLimitsProvider(),
            config,
            telemetry,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FinalizeTransferHandler>.Instance);
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesTransfer_FileItems_BlobRefs_and_copies_blobs()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, ContentType = "text/plain", Retries = 0 };
        var file2 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "b.png", SizeBytes = 200, ContentType = "image/png", Retries = 1 };

        // Seed staging blobs.
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file2.FileId)] = new byte[200];

        var result = await _handler.Handle(new FinalizeTransferCommand(draftId, "key-1", [file1, file2]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Status); // Draft
        Assert.Equal(300, result.TotalBytes);
        Assert.Equal(2, result.Files.Count);
        Assert.StartsWith("http://localhost:5173/t/", result.PublicUrl);
        Assert.EndsWith(result.LinkId, result.PublicUrl);

        // DB assertions via the stub store.
        var saved = _store.SavedTransfer;
        Assert.NotNull(saved);
        Assert.Equal(0, saved!.Status);
        Assert.Equal(300, saved.TotalBytes);
        Assert.Equal(2, saved.FileCount);
        Assert.Equal("Guest", saved.SenderName);
        Assert.Equal(1, saved.MaxDownloads);

        Assert.Equal(2, _store.SavedFileItems.Count);
        // T-003-09-fix: FileItem.Id must equal the request's FileId — finalize copies blobs to BlobPaths.TransferFile(transferId,
        // file.FileId), so endpoint 6's minted SAS (keyed on FileItem.Id) only resolves when the two match.
        Assert.Equal(file1.FileId, _store.SavedFileItems[0].Id);
        Assert.Equal(file2.FileId, _store.SavedFileItems[1].Id);
        Assert.Equal(0, _store.SavedFileItems[0].SortOrder);
        Assert.Equal("a.txt", _store.SavedFileItems[0].OriginalName);
        Assert.Equal(100, _store.SavedFileItems[0].SizeBytes);
        Assert.Equal(1, _store.SavedFileItems[1].SortOrder);
        Assert.Equal("b.png", _store.SavedFileItems[1].OriginalName);

        Assert.Equal(2, _store.SavedBlobRefs.Count);
        Assert.All(_store.SavedBlobRefs, br => Assert.Equal(1, br.RefCount));

        // Blob copy assertions.
        Assert.True(_blobStore.Uploads.ContainsKey(BlobPaths.TransferFile(result.TransferId, file1.FileId)));
        Assert.True(_blobStore.Uploads.ContainsKey(BlobPaths.TransferFile(result.TransferId, file2.FileId)));
    }

    [Fact]
    public async Task Handle_IdempotentReplay_ReturnsIdenticalResult_without_duplicate_blob_copy()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, ContentType = "text/plain", Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        var cmd = new FinalizeTransferCommand(draftId, "key-idem", [file1]);
        var result1 = await _handler.Handle(cmd, CancellationToken.None);
        Assert.NotNull(result1);

        // Second call with same idempotency key — the stub store now has the idempotency key stored.
        var result2 = await _handler.Handle(cmd, CancellationToken.None);
        Assert.NotNull(result2);
        Assert.Equal(result1!.TransferId, result2!.TransferId);
        Assert.Equal(result1.LinkId, result2.LinkId);
        Assert.Equal(result1.PublicUrl, result2.PublicUrl);
        Assert.Equal(result1.TotalBytes, result2.TotalBytes);

        // Only one save (one Transfer row).
        Assert.Single(_store.SavedTransfers);

        // Blob was copied only once.
        var uploadPath = BlobPaths.TransferFile(result1.TransferId, file1.FileId);
        Assert.Single(_blobStore.Uploads, kv => kv.Key == uploadPath);
    }

    [Fact]
    public async Task Handle_ZeroFiles_ReturnsNull()
    {
        var draftId = Guid.NewGuid();
        var result = await _handler.Handle(new FinalizeTransferCommand(draftId, "key-empty", []), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_BlobMissing_Throws_and_no_Transfer_saved()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };
        var file2 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "b.txt", SizeBytes = 50, Retries = 0 };

        // Only one blob seeded; the other is missing.
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];
        // No blob for file2.

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _handler.Handle(new FinalizeTransferCommand(draftId, "key-missing", [file1, file2]), CancellationToken.None));

        // No transfer saved (blob copy failed before DB write).
        Assert.Empty(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_LinkIdCollision_FirstRegenerates_SucceedsOnSecond()
    {
        _store.CollideOnce = true;

        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, ContentType = "text/plain", Retries = 0 };
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        var result = await _handler.Handle(new FinalizeTransferCommand(draftId, "key-collide-retry", [file1]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_LinkIdCollision_SecondCollision_Throws_InternalTelemetry()
    {
        _store.AlwaysCollide = true;

        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, ContentType = "text/plain", Retries = 0 };
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _handler.Handle(new FinalizeTransferCommand(draftId, "key-collide", [file1]), CancellationToken.None));

        Assert.Contains("LinkId collision", ex.Message);
        Assert.Empty(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_Success_EmitsUploadCompleted_with_correct_values()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 2 };
        var file2 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "b.png", SizeBytes = 200, Retries = 3 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file2.FileId)] = new byte[200];

        await _handler.Handle(new FinalizeTransferCommand(draftId, "key-telemetry", [file1, file2]), CancellationToken.None);

        // upload_completed {bytes} bytes in {durationMs} ms after {retries} retries
        var entry = _logger.Entries.Single(e => e.RenderedText.StartsWith("upload_completed"));
        Assert.Contains("300", entry.RenderedText); // totalBytes = 100 + 200
        Assert.Contains("5", entry.RenderedText);   // totalRetries = 2 + 3
    }

    [Fact]
    public async Task Handle_StorageQuotaExceeded_Throws_WithNumbers()
    {
        var ownerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 200_000_000, Retries = 0 }; // 200 MB

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[200_000_000];

        // Stub: active storage is 5.2 GB (just under 5 GB quota), adding 200 MB pushes over.
        _store.ActiveStorageBytes = 5_200_000_000;

        var ex = await Assert.ThrowsAsync<StorageQuotaExceededException>(() =>
            _handler.Handle(new FinalizeTransferCommand(draftId, "key-quota", [file1], ownerId), CancellationToken.None));

        Assert.Equal(5_200_000_000, ex.CurrentBytes);
        Assert.Equal(200_000_000, ex.NewTransferBytes);
        Assert.Equal(5_368_709_120, ex.QuotaBytes); // 5 GB from StubLimitsProvider default
        Assert.Contains("Storage full", ex.Message);
        Assert.Empty(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_ActiveTransfersAtLimit_Throws_NamesLimit()
    {
        var ownerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        // Stub: active count is at the limit (20).
        _store.ActiveTransferCount = 20;

        var ex = await Assert.ThrowsAsync<ActiveTransfersExceededException>(() =>
            _handler.Handle(new FinalizeTransferCommand(draftId, "key-active", [file1], ownerId), CancellationToken.None));

        Assert.Equal(20, ex.CurrentCount);
        Assert.Equal(20, ex.MaxActiveTransfers);
        Assert.Contains("You have 20 active transfers (limit 20).", ex.Message);
        Assert.Empty(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_BlobSizeMismatch_Throws_NamesFile()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        // Blob is actually 300 bytes but declared as 100.
        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[300];

        var ex = await Assert.ThrowsAsync<BlobSizeMismatchException>(() =>
            _handler.Handle(new FinalizeTransferCommand(draftId, "key-mismatch", [file1]), CancellationToken.None));

        Assert.Equal("a.txt", ex.FileName);
        Assert.Equal(100, ex.DeclaredBytes);
        Assert.Equal(300, ex.ActualBytes);
        Assert.Contains("File 'a.txt' is 300 bytes on the server but was declared as 100 bytes.", ex.Message);
        Assert.Empty(_store.SavedTransfers);
    }

    [Fact]
    public async Task Handle_Guest_SkipsAccountChecks()
    {
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        // Stub that throws if called — proves guests skip account checks.
        var throwingStore = new ThrowingTransferStore();
        var handler = new FinalizeTransferHandler(
            throwingStore,
            _blobStore,
            new StubLimitsProvider(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Url:Public"] = "http://localhost:5173" })
                .Build(),
            new WaTelemetryReporter(_logger, Encoding.UTF8.GetBytes("test-key")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FinalizeTransferHandler>.Instance);

        var result = await handler.Handle(new FinalizeTransferCommand(draftId, "key-guest", [file1]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, result!.Status); // Draft
    }

    [Fact]
    public async Task StorageBytesActive_EmittedWithPriorPlusNewBytes_ForAccountUser()
    {
        var ownerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        // T-007-05: meter value = the law's exact comparison value (prior active + this transfer).
        _store.ActiveStorageBytes = 2_500_000_000;

        await _handler.Handle(new FinalizeTransferCommand(draftId, "key-meter", [file1], ownerId), CancellationToken.None);

        var entry = _logger.Entries.Single(e => e.RenderedText.StartsWith("storage_bytes_active"));
        Assert.Contains("2500000100", entry.RenderedText); // 2_500_000_000 + 100 — no second query, law and meter share one read
    }

    [Fact]
    public async Task StorageMeter_ValueEqualsQuotaLawExpression_AtFullQuota()
    {
        // AC-007-2 (meter half): an account at 5 GB of 5 GB reads a full meter — storage_bytes_active == STORAGE_QUOTA.
        // The plan referenced T-007-03's gherkin pair (4.9 GB + 200 MB rejected; 4 GB + 900 MB succeeds) as reusable, but only the
        // rejection test (Handle_StorageQuotaExceeded_Throws_WithNumbers, 5.2 GB > 5 GB quota) exists in this file; the pair was
        // never committed. This test implements TESTS #1 from scratch with the same pin: one GetActiveStorageBytesAsync value
        // drives both the law (quota check) and the meter — a zero-byte finalize keeps the law green so both reads are observable.
        var ownerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "empty.txt", SizeBytes = 0, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = []; // 0-byte blob; declared == actual (blob size truth)

        // Account at exactly the Free STORAGE_QUOTA (T-007-01's LimitsProviderTests pins 5_368_709_120 for the same plan).
        _store.ActiveStorageBytes = 5_368_709_120;

        var result = await _handler.Handle(new FinalizeTransferCommand(draftId, "key-full-quota", [file1], ownerId), CancellationToken.None);

        Assert.NotNull(result); // law: 5 GB + 0 <= 5 GB — at-full-quota does not reject (strict >)
        var entry = _logger.Entries.Single(e => e.RenderedText.StartsWith("storage_bytes_active"));
        Assert.Equal("storage_bytes_active 5368709120", entry.RenderedText); // meter == quota law's read == STORAGE_QUOTA → 100 percent
    }

    [Fact]
    public async Task StorageBytesActive_NotEmitted_ForGuest()
    {
        // EC-007-3 guard mirror: no owner → no account-level read at all → no meter.
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];

        var result = await _handler.Handle(new FinalizeTransferCommand(draftId, "key-guest-meter", [file1]), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(_logger.Entries.Where(e => e.RenderedText.StartsWith("storage_bytes_active")));
    }

    [Fact]
    public async Task StorageBytesActive_NotEmitted_OnIdempotentReplay()
    {
        // EC-002-1: replay returns the cached result before any mutation — so no emission on the second call.
        var ownerId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var file1 = new FinalizeFile { FileId = Guid.NewGuid(), Name = "a.txt", SizeBytes = 100, Retries = 0 };

        _blobStore.Blobs[BlobPaths.StagingFile(draftId, file1.FileId)] = new byte[100];
        _store.ActiveStorageBytes = 2_500_000_000;

        var cmd = new FinalizeTransferCommand(draftId, "key-replay-meter", [file1], ownerId);
        await _handler.Handle(cmd, CancellationToken.None); // first call — emits once
        Assert.Single(_logger.Entries, e => e.RenderedText.StartsWith("storage_bytes_active"));

        await _handler.Handle(cmd, CancellationToken.None); // replay — cached result, no mutation, no re-emission
        Assert.Single(_logger.Entries, e => e.RenderedText.StartsWith("storage_bytes_active"));
        Assert.Single(_store.SavedTransfers);
    }

    // ---- Stubs ----

    private sealed class StubTransferStore : ITransferStore
    {
        public List<Transfer> SavedTransfers { get; } = [];
        public List<BlobRef> SavedBlobRefs { get; } = [];
        public List<FileItem> SavedFileItems { get; } = [];
        public List<IdempotencyKey> SavedIdempotencyKeys { get; } = [];

        /// <summary>Force every mint to collide (second-collision path).</summary>
        public bool AlwaysCollide { get; set; }

        /// <summary>Force exactly the first mint to collide, then succeed (regenerate-once path).</summary>
        public bool CollideOnce { get; set; }

        private int _linkIdChecks;

        public Transfer? SavedTransfer => SavedTransfers.LastOrDefault();

        // T-007-03: account-level checks — default 0/0 for existing tests.
        public long ActiveStorageBytes { get; set; } = 0;
        public int ActiveTransferCount { get; set; } = 0;

        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult(SavedIdempotencyKeys.FirstOrDefault(k => k.Key == key));

        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct)
        {
            var call = _linkIdChecks++;
            if (AlwaysCollide) return Task.FromResult(true);
            if (CollideOnce && call == 0) return Task.FromResult(true);
            return Task.FromResult(SavedTransfers.Any(t => t.LinkId == linkId));
        }

        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct)
        {
            SavedTransfers.Add(transfer);
            SavedBlobRefs.AddRange(blobRefs);
            SavedFileItems.AddRange(fileItems);
            SavedIdempotencyKeys.Add(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(ActiveStorageBytes);

        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
            Task.FromResult(ActiveTransferCount);
    }

    private sealed class StubLimitsProvider : ILimitsProvider
    {
        public LimitsRecord Record { get; set; } = new()
        {
            MaxTransferSize = 5_368_709_120, // 5 GB
            MaxSingleFile = 5_368_709_120,
            StorageQuota = 5_368_709_120, // 5 GB
            ActiveTransfersMax = 20
        };

        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record);
    }

    private sealed class ThrowingTransferStore : ITransferStore
    {
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
            Task.FromResult<IdempotencyKey?>(null);

        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) =>
            throw new InvalidOperationException("GetActiveStorageBytesAsync called for guest — should not be called.");

        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) =>
            throw new InvalidOperationException("GetActiveTransferCountAsync called for guest — should not be called.");
    }

    private sealed class StubBlobStore : IBlobStore
    {
        public Dictionary<string, byte[]> Blobs { get; } = new();
        public Dictionary<string, byte[]> Uploads { get; } = new();

        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            Uploads[path] = ms.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!Blobs.TryGetValue(path, out var bytes))
                throw new KeyNotFoundException($"Blob not found: {path}");
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.ContainsKey(path));

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            Blobs.Remove(path);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(formatter(state, exception)));
        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args)
            => Entries.Add(new Entry(message ?? string.Empty));
    }
}
