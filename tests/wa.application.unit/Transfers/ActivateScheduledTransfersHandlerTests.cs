using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

/// <summary>US-022-04 (FR-022-5, AC-022-1, AC-022-5, AC-022-6, EC-022-3): f-expire schedule sub-step.</summary>
public class ActivateScheduledTransfersHandlerTests
{
    private static readonly DateTime Now = new(2026, 12, 1, 12, 7, 30, DateTimeKind.Utc);

    private readonly StubScheduledSendStore _store = new();
    private readonly StubEventPublisher _publisher = new();
    private readonly ActivateScheduledTransfersHandler _handler;

    public ActivateScheduledTransfersHandlerTests() =>
        _handler = new ActivateScheduledTransfersHandler(_store, _publisher, NullLogger<ActivateScheduledTransfersHandler>.Instance);

    private static ActivatedTransfer Activated(Guid id) =>
        new(id, "LINK" + id.ToString("N")[..4], 2048, 2, "s@x.com", Now.AddMinutes(-5), ["a@x.com", "b@y.com"]);

    private void Due(params Guid[] ids)
    {
        _store.Batches.Add(ids.Select(i => new DueScheduledRow(i, Now.AddMinutes(-5))).ToList());
        foreach (var id in ids) _store.Activations[id] = Activated(id);
    }

    [Fact]
    public async Task Run_DueRow_Activated_PublishesOneTransferCreated_SamePayloadShape()
    {
        var id = Guid.NewGuid();
        Due(id);

        var count = await _handler.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, count);
        var env = Assert.Single(_publisher.Published);
        Assert.Equal(EventType.TransferCreated, env.Type);
        Assert.Equal(1, env.Version);
        Assert.Equal(Activated(id).LinkId, env.PartitionKey);
        Assert.Equal(Now, env.OccurredAtUtc);
        var json = System.Text.Json.JsonSerializer.Serialize(env.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(id, root.GetProperty("transferId").GetGuid());
        Assert.Equal(2, root.GetProperty("emails").GetArrayLength());
        Assert.Equal(Now.AddMinutes(-5), root.GetProperty("scheduledSendAt").GetDateTime().ToUniversalTime());
        foreach (var name in new[] { "linkId", "totalBytes", "fileCount", "senderEmail" })
            Assert.True(root.TryGetProperty(name, out _), name);
    }

    [Fact]
    public async Task Run_ClaimLost_ReturnsZero_NoPublish()
    {
        _store.ClaimResult = false;
        Due(Guid.NewGuid());

        Assert.Equal(0, await _handler.RunAsync(Now, CancellationToken.None));
        Assert.Empty(_publisher.Published);
        Assert.False(_store.ClaimCompleted);
    }

    [Fact]
    public async Task Run_TryActivateReturnsNull_NoPublish()
    {
        var id = Guid.NewGuid();
        Due(id);
        _store.Activations.Remove(id); // expired, deleted or already flipped

        Assert.Equal(0, await _handler.RunAsync(Now, CancellationToken.None));
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Run_ZeroActivationsInBatch_StopsLoop()
    {
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        Due(ids);
        foreach (var id in ids) _store.Activations.Remove(id);
        Due(Guid.NewGuid()); // would be reached only if the loop did not stop

        await _handler.RunAsync(Now, CancellationToken.None);

        Assert.Equal(1, _store.GetDueCalls);
    }

    [Fact]
    public async Task Run_MultipleBatches_AllActivated()
    {
        var first = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToArray(); // full batch: loop continues
        var second = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Due(first);
        Due(second);

        var count = await _handler.RunAsync(Now, CancellationToken.None);

        Assert.Equal(502, count);
        Assert.Equal(502, _publisher.Published.Count);
    }

    [Fact]
    public async Task Run_ClaimCompleted_EvenWhenPublishThrows()
    {
        Due(Guid.NewGuid());
        _publisher.Throw = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.RunAsync(Now, CancellationToken.None));

        Assert.True(_store.ClaimCompleted);
    }

    [Fact]
    public async Task Run_ClaimWindowIsTruncatedTo15Min_LockNowPlus10Min()
    {
        await _handler.RunAsync(Now, CancellationToken.None);

        Assert.Equal(new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc), _store.ClaimWindow);
        Assert.Equal(Now.AddMinutes(10), _store.ClaimLockUntil);
    }

    // ---- Stubs ----

    private sealed class StubScheduledSendStore : IScheduledSendStore
    {
        public bool ClaimResult { get; set; } = true;
        public bool ClaimCompleted { get; private set; }
        public DateTime? ClaimWindow { get; private set; }
        public DateTime? ClaimLockUntil { get; private set; }
        public int GetDueCalls { get; private set; }
        public List<List<DueScheduledRow>> Batches { get; } = [];
        public Dictionary<Guid, ActivatedTransfer> Activations { get; } = [];

        public Task<bool> TryClaimAsync(DateTime runWindowStartUtc, DateTime lockUntilUtc, CancellationToken ct)
        {
            ClaimWindow = runWindowStartUtc;
            ClaimLockUntil = lockUntilUtc;
            return Task.FromResult(ClaimResult);
        }

        public Task<IReadOnlyList<DueScheduledRow>> GetDueBatchAsync(DateTime nowUtc, int batchSize, CancellationToken ct)
        {
            var i = GetDueCalls++;
            return Task.FromResult<IReadOnlyList<DueScheduledRow>>(i < Batches.Count ? Batches[i] : []);
        }

        public Task<ActivatedTransfer?> TryActivateAsync(Guid transferId, DateTime nowUtc, CancellationToken ct) =>
            Task.FromResult(Activations.GetValueOrDefault(transferId));

        public Task CompleteClaimAsync(DateTime runWindowStartUtc, DateTime finishedAtUtc, CancellationToken ct)
        {
            ClaimCompleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];
        public bool Throw { get; set; }

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            if (Throw) throw new InvalidOperationException("broker down");
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }
}
