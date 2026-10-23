using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-005-01: f-expire handler — batch loop, JobRun claim, tolerance window, event emission.
public class ExpireTransfersHandlerTests
{
    private readonly StubExpiryStore _store;
    private readonly StubEventPublisher _eventPublisher;
    private readonly ExpireTransfersHandler _handler;

    public ExpireTransfersHandlerTests()
    {
        _store = new StubExpiryStore();
        _eventPublisher = new StubEventPublisher();
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[32]);
        _handler = new ExpireTransfersHandler(_store, _eventPublisher, telemetry, NullLogger<ExpireTransfersHandler>.Instance);
    }

    [Fact]
    public async Task Run_HappyPath_ThreeTransfers_EmitsThreeEvents()
    {
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = new List<List<ExpiryBatchRow>>
        {
            new()
            {
                new ExpiryBatchRow(Guid.NewGuid(), "ABCD1234"),
                new ExpiryBatchRow(Guid.NewGuid(), "EFGH5678"),
                new ExpiryBatchRow(Guid.NewGuid(), "IJKL9012")
            }
        };

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal(3, _eventPublisher.Published.Count);
        Assert.All(_eventPublisher.Published, e => Assert.Equal(EventType.TransferExpired, e.Type));
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
        // Simulate 3 batches: 500 + 500 + 200.
        var batch1 = Enumerable.Range(0, 500).Select(i => new ExpiryBatchRow(Guid.NewGuid(), $"L{i:D6}")).ToList();
        var batch2 = Enumerable.Range(500, 500).Select(i => new ExpiryBatchRow(Guid.NewGuid(), $"L{i:D6}")).ToList();
        var batch3 = Enumerable.Range(1000, 200).Select(i => new ExpiryBatchRow(Guid.NewGuid(), $"L{i:D6}")).ToList();
        _store.Batches = [batch1, batch2, batch3];

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(1200, count);
        Assert.Equal(1200, _eventPublisher.Published.Count);
    }

    [Fact]
    public async Task Run_ToleranceWindow_TransferWithinTolerance_NotExpired()
    {
        // The handler passes cutoff = now - 2min to the store.
        // A transfer with ExpiresAtUtc = now - 1min is BEFORE the cutoff, so it's NOT in the batch.
        var now = DateTime.UtcNow;
        _store.ClaimResult = true;
        _store.Batches = [[]]; // no rows match (all within tolerance)

        var count = await _handler.RunAsync(now, CancellationToken.None);

        Assert.Equal(0, count);
        // The cutoff passed to the store should be now - 2min.
        Assert.True(_store.LastCutoff.HasValue);
        var expectedCutoff = now.AddMinutes(-2).AddSeconds(5); // small tolerance for DateTime precision
        Assert.InRange(_store.LastCutoff.Value, now.AddMinutes(-2).AddSeconds(-5), expectedCutoff);
    }

    [Fact]
    public void TruncateTo15Min_VariousTimes_Correct()
    {
        Assert.Equal(new DateTime(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc), ExpireTransfersHandler.TruncateTo15Min(new DateTime(2025, 6, 15, 10, 3, 45, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 10, 15, 0, DateTimeKind.Utc), ExpireTransfersHandler.TruncateTo15Min(new DateTime(2025, 6, 15, 10, 27, 30, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 10, 45, 0, DateTimeKind.Utc), ExpireTransfersHandler.TruncateTo15Min(new DateTime(2025, 6, 15, 10, 45, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc), ExpireTransfersHandler.TruncateTo15Min(new DateTime(2025, 6, 15, 10, 44, 59, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc), ExpireTransfersHandler.TruncateTo15Min(new DateTime(2025, 6, 15, 0, 14, 59, DateTimeKind.Utc)));
    }

    // ---- Stubs ----

    private sealed class StubExpiryStore : IExpiryStore
    {
        public bool ClaimResult { get; set; } = true;
        public List<List<ExpiryBatchRow>> Batches { get; set; } = [];
        public DateTime? LastCutoff { get; private set; }
        public bool ClaimCompleted { get; private set; }

        private int _batchIndex;

        public Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct) =>
            Task.FromResult(ClaimResult);

        public Task<IReadOnlyList<ExpiryBatchRow>> GetNextBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        {
            LastCutoff = cutoffUtc;
            if (_batchIndex < Batches.Count)
                return Task.FromResult<IReadOnlyList<ExpiryBatchRow>>(Batches[_batchIndex++]);
            return Task.FromResult<IReadOnlyList<ExpiryBatchRow>>([]);
        }

        public Task<int> MarkExpiredAsync(IReadOnlyList<Guid> transferIds, DateTime expiredAtUtc, CancellationToken ct) =>
            Task.FromResult(transferIds.Count); // all rows updated (guard Status==1 passes in stub)

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
}
