using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-005-02: f-delete-transfers handler — batch loop, JobRun claim, event emission with reason.
public class DeleteExpiredTransfersHandlerTests
{
    private readonly StubExpiryDeletionStore _store;
    private readonly StubEventPublisher _eventPublisher;
    private readonly StubBlobStore _blobStore;
    private readonly DeleteExpiredTransfersHandler _handler;

    public DeleteExpiredTransfersHandlerTests()
    {
        _store = new StubExpiryDeletionStore();
        _eventPublisher = new StubEventPublisher();
        _blobStore = new StubBlobStore();
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[32]);
        _handler = new DeleteExpiredTransfersHandler(_store, _eventPublisher, _blobStore, telemetry, NullLogger<DeleteExpiredTransfersHandler>.Instance);
    }

    [Fact]
    public async Task Run_HappyPath_TwoTransfers_EmitsTwoEvents_WithReason()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = new List<List<TransferDeletionBatchRow>>
        {
            new()
            {
                new TransferDeletionBatchRow(Guid.NewGuid(), "ABCD1234"),
                new TransferDeletionBatchRow(Guid.NewGuid(), "EFGH5678")
            }
        };

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal(2, _eventPublisher.Published.Count);
        Assert.All(_eventPublisher.Published, e => Assert.Equal(EventType.TransferDeleted, e.Type));
        // Verify reason in payload — serialize to JSON and check.
        var json = System.Text.Json.JsonSerializer.SerializeToElement(_eventPublisher.Published[0].Payload!);
        Assert.Equal("expiry-grace", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Run_ClaimLost_ReturnsZero_NoEvents()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = false;

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(_eventPublisher.Published);
    }

    [Fact]
    public async Task Run_EmptyBatch_ReturnsZero_CompletesClaim()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = [[]]; // first batch empty → loop exits immediately

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(_eventPublisher.Published);
        Assert.True(_store.ClaimCompleted);
    }

    [Fact]
    public async Task Run_MultipleBatches_AllRowsProcessed()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        // Simulate 2 batches: 100 + 50.
        var batch1 = Enumerable.Range(0, 100).Select(i => new TransferDeletionBatchRow(Guid.NewGuid(), $"L{i:D6}")).ToList();
        var batch2 = Enumerable.Range(100, 50).Select(i => new TransferDeletionBatchRow(Guid.NewGuid(), $"L{i:D6}")).ToList();
        _store.Batches = [batch1, batch2];

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(150, count);
        Assert.Equal(150, _eventPublisher.Published.Count);
    }

    [Fact]
    public async Task Run_BlobNotFound_Swallowed_NoError()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = new List<List<TransferDeletionBatchRow>>
        {
            new() { new TransferDeletionBatchRow(Guid.NewGuid(), "ABCD1234") }
        };
        // Blob deletion throws — should be swallowed.
        _blobStore.ShouldThrow = true;

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Single(_eventPublisher.Published);
    }

    [Fact]
    public async Task Run_CutoffIsNowMinusGraceDays()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = [[]]; // no rows — just checking the cutoff passed to store.

        await _handler.RunAsync(now, CancellationToken.None);

        Assert.True(_store.LastCutoff.HasValue);
        var expectedCutoff = now.AddDays(-3);
        Assert.InRange(_store.LastCutoff.Value, expectedCutoff.AddSeconds(-1), expectedCutoff.AddSeconds(1));
    }

    // ---- Stubs ----

    private sealed class StubExpiryDeletionStore : IExpiryDeletionStore
    {
        public bool ClaimResult { get; set; } = true;
        public List<List<TransferDeletionBatchRow>> Batches { get; set; } = [];
        public DateTime? LastCutoff { get; private set; }
        public bool ClaimCompleted { get; private set; }

        private int _batchIndex;

        public Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct) =>
            Task.FromResult(ClaimResult);

        public Task<IReadOnlyList<TransferDeletionBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            LastCutoff = cutoffUtc;
            if (_batchIndex < Batches.Count)
                return Task.FromResult<IReadOnlyList<TransferDeletionBatchRow>>(Batches[_batchIndex++]);
            return Task.FromResult<IReadOnlyList<TransferDeletionBatchRow>>([]);
        }

        public Task<int> DeleteTransfersAsync(IReadOnlyList<Guid> transferIds, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult(transferIds.Count); // all rows updated in stub.

        public Task CompleteClaimAsync(DateTime runWindowStartUtc, CancellationToken ct)
        {
            ClaimCompleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    private sealed class StubBlobStore : IBlobStore
    {
        public bool ShouldThrow { get; set; }

        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Stream.Null);
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
            ShouldThrow ? Task.FromException(new InvalidOperationException("BlobNotFound")) : Task.CompletedTask;
    }
}
