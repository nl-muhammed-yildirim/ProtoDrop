using Microsoft.Extensions.Logging;
using wa.application.UseCases.Gdpr;
using wa.infrastructure.Telemetry;

namespace wa.application.unit.Gdpr;

/// <summary>US-047-02 (AC-047-2, EC-047-4, EC-047-5): the f-gdpr-sweep handler over stubs.</summary>
public class SweepErasedTelemetryHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 2, 0, 5, DateTimeKind.Utc);

    private sealed class StubStore : IGdprSweepStore
    {
        public bool ClaimResult { get; set; } = true;
        public List<Guid> Due { get; } = [];
        public bool Completed { get; private set; }
        public DateTime? DeletedBefore { get; private set; }
        public int? Batch { get; private set; }
        public List<(Guid Id, DateTime At)> Swept { get; } = [];

        public Task<bool> TryClaimAsync(DateTime windowStartUtc, DateTime lockUntilUtc, CancellationToken ct) => Task.FromResult(ClaimResult);
        public Task CompleteClaimAsync(DateTime windowStartUtc, CancellationToken ct) { Completed = true; return Task.CompletedTask; }
        public Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deletedBeforeUtc, int batchSize, CancellationToken ct)
        {
            DeletedBefore = deletedBeforeUtc;
            Batch = batchSize;
            return Task.FromResult<IReadOnlyList<Guid>>(Due.ToList());
        }
        public Task MarkSweptAsync(Guid userId, DateTime sweptAtUtc, CancellationToken ct) { Swept.Add((userId, sweptAtUtc)); return Task.CompletedTask; }
    }

    private sealed class RecordingEraser : ITelemetryEraser
    {
        public List<(Guid Id, DateTime Since)> Calls { get; } = [];
        public Func<Guid, Exception?> Fail { get; set; } = _ => null;

        public Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct)
        {
            Calls.Add((userId, sinceUtc));
            var ex = Fail(userId);
            return ex is null ? Task.CompletedTask : Task.FromException(ex);
        }
    }

    private static (SweepErasedTelemetryHandler Handler, StubStore Store, RecordingEraser Eraser, CapturingLogger<SweepErasedTelemetryHandler> Log) Build()
    {
        var store = new StubStore();
        var eraser = new RecordingEraser();
        var log = new CapturingLogger<SweepErasedTelemetryHandler>();
        return (new SweepErasedTelemetryHandler(store, eraser, log), store, eraser, log);
    }

    [Fact]
    public async Task ClaimLost_ReturnsZero_EraserNeverCalled()
    {
        var (h, store, eraser, _) = Build();
        store.ClaimResult = false;
        store.Due.Add(Guid.NewGuid());

        Assert.Equal(0, await h.RunAsync(Now, CancellationToken.None));
        Assert.Empty(eraser.Calls);
        Assert.Empty(store.Swept);
        Assert.False(store.Completed); // never claimed, nothing to complete
    }

    [Fact]
    public async Task DueMarker_ErasedThenMarkedSwept_AtRunTime() // AC-047-2
    {
        var (h, store, eraser, _) = Build();
        var id = Guid.NewGuid();
        store.Due.Add(id);

        Assert.Equal(1, await h.RunAsync(Now, CancellationToken.None));

        Assert.Equal(Now - TimeSpan.FromHours(24), store.DeletedBefore);
        Assert.Equal(500, store.Batch);
        Assert.Equal([(id, Now - TimeSpan.FromDays(90))], eraser.Calls);
        Assert.Equal([(id, Now)], store.Swept);
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task NoDueMarkers_MarksNothing() // AC-047-2 block 2: idempotent second night
    {
        var (h, store, eraser, _) = Build();

        Assert.Equal(0, await h.RunAsync(Now, CancellationToken.None));
        Assert.Empty(eraser.Calls);
        Assert.Empty(store.Swept);
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task EraserUnavailable_MarksNothing_WarnsAndStops() // EC-047-4
    {
        var (h, store, eraser, log) = Build();
        store.Due.AddRange([Guid.NewGuid(), Guid.NewGuid()]);
        eraser.Fail = _ => new TelemetryUnavailableException("down");

        Assert.Equal(0, await h.RunAsync(Now, CancellationToken.None));

        Assert.Empty(store.Swept);
        Assert.Single(eraser.Calls);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("2 markers stay pending", warning.Message);
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task OtherFailure_OnOneMarker_NextMarkerStillSwept()
    {
        var (h, store, eraser, log) = Build();
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        store.Due.AddRange([bad, good]);
        eraser.Fail = id => id == bad ? new InvalidOperationException("boom") : null;

        Assert.Equal(1, await h.RunAsync(Now, CancellationToken.None));

        Assert.Equal([(good, Now)], store.Swept);
        Assert.Equal(2, eraser.Calls.Count);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
        Assert.True(store.Completed);
    }

    [Fact]
    public async Task EraserReceivesMarkerUserIdOnly() // EC-047-5
    {
        var (h, store, eraser, _) = Build();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        store.Due.AddRange(ids);

        await h.RunAsync(Now, CancellationToken.None);

        Assert.Equal(ids, eraser.Calls.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task NoLogEntryMentionsTheUserId()
    {
        var (h, store, eraser, log) = Build();
        var bad = Guid.NewGuid();
        var down = Guid.NewGuid();
        store.Due.AddRange([bad, down]);
        eraser.Fail = id => id == bad ? new InvalidOperationException("boom") : new TelemetryUnavailableException("down");

        await h.RunAsync(Now, CancellationToken.None);

        Assert.NotEmpty(log.Entries);
        Assert.False(log.Mentions(bad.ToString()));
        Assert.False(log.Mentions(down.ToString()));
    }

    [Fact]
    public async Task ClosedSetTelemetryEraser_Completes()
    {
        await new ClosedSetTelemetryEraser().EraseUserAsync(Guid.NewGuid(), Now, CancellationToken.None);
    }
}
