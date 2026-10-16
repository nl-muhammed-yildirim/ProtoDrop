using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Transfers;

// T-001-05: the handler is the only business logic behind POST /api/v1/transfers/draft (TA-4.2a). Free guest limits are resolved via ILimitsProvider — never literals — and the whole selection is checked against MAX_TRANSFER_SIZE + per-file MAX_SINGLE_FILE before minting one staging SAS per file. Null result = oversized; upload_started emits exactly once on success.
public class CreateDraftHandlerTests
{
    private const long FreeMax = 5_368_709_120L; // Part-2 free-plan limit (T-052-04), mirrored so the test is self-contained.

    private static LimitsRecord FreeLimits() => new()
    {
        MaxTransferSize = FreeMax,
        MaxSingleFile = FreeMax,
    };

    [Fact]
    public async Task Handle_ValidSelection_ReturnsResultWithOneSasPerFileAndDistinctFileIds()
    {
        var f = CreateFixture(FreeLimits());
        var files = new[]
        {
            new CreateDraftFile { Name = "a.txt", SizeBytes = 100 },
            new CreateDraftFile { Name = "b.png", SizeBytes = 200 },
            new CreateDraftFile { Name = "c.mp4", SizeBytes = FreeMax - 300 }, // at-limit total still passes (see boundary test).
        };

        var result = await f.Handler.Handle(new CreateDraftCommand(files), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(7200, result!.ExpiresInSec); // TA-3.6's 2 h draft-upload TTL — constant, not a plan limit.
        Assert.Equal(files.Length, result.Files.Count);
        Assert.Equal(result.Files.Count, result.Files.Select(x => x.FileId).Distinct().Count()); // one mint per file, all distinct (FR: fresh fileId per staged file).
        Assert.Equal(files.Length, f.Minter.Mints.Count);
        foreach (var (mintDraft, _) in f.Minter.Mints)
            Assert.Equal(result.DraftId.ToString("n"), mintDraft); // every mint is addressed to the same draft.
    }

    [Fact]
    public async Task Handle_TotalExceedsMaxTransferSize_ReturnsNull() // TA-4.1.3 TRANSFER_SIZE_EXCEEDED path — endpoint maps null → 422.
    {
        var f = CreateFixture(FreeLimits());

        var result = await f.Handler.Handle(new CreateDraftCommand([
            new CreateDraftFile { Name = "big.mp4", SizeBytes = FreeMax },
            new CreateDraftFile { Name = "one-byte.bin", SizeBytes = 1 }]), // total = MaxTransferSize + 1.
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_SingleFileExceedsMaxSingleFile_ReturnsNull()
    {
        var f = CreateFixture(FreeLimits());

        var result = await f.Handler.Handle(new CreateDraftCommand([
            new CreateDraftFile { Name = "huge.bin", SizeBytes = FreeMax + 1 }]), // total also exceeds, but the per-file check is what this pins.
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Handle_TotalExactlyAtLimit_ReturnsResult() // boundary: strict > — exactly at the limit passes (unit-pinned).
    {
        var f = CreateFixture(FreeLimits());

        var result = await f.Handler.Handle(new CreateDraftCommand([
            new CreateDraftFile { Name = "half.bin", SizeBytes = FreeMax / 2 },
            new CreateDraftFile { Name = "rest.bin", SizeBytes = FreeMax - (FreeMax / 2) }]), // total == MaxTransferSize exactly.
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Handle_Success_EmitsUploadStartedExactlyOnce() // TA-10.2 closed name — the only event this use case emits.
    {
        var f = CreateFixture(FreeLimits());

        await f.Handler.Handle(new CreateDraftCommand([new CreateDraftFile { Name = "a.txt", SizeBytes = 1 }]), CancellationToken.None);

        Assert.Single(f.Logger.Entries, e => e.RenderedText == TelemetryEvents.UploadStarted);
    }

    [Fact]
    public async Task Handle_Oversized_DoesNotEmitUploadStarted() // no half-emitted event when the selection is rejected.
    {
        var f = CreateFixture(FreeLimits());

        await f.Handler.Handle(new CreateDraftCommand([new CreateDraftFile { Name = "big.bin", SizeBytes = FreeMax + 1 }]), CancellationToken.None);

        Assert.DoesNotContain(f.Logger.Entries, e => e.RenderedText == TelemetryEvents.UploadStarted);
    }

    private sealed record Fixture(CreateDraftHandler Handler, StubMinter Minter, RecordingLogger Logger);

    private static Fixture CreateFixture(LimitsRecord limits)
    {
        var logger = new RecordingLogger();
        var minter = new StubMinter();
        // Real reporter over the recording logger — PII slots + closed-name gate are exercised for real.
        var handler = new CreateDraftHandler(
            new StubLimitsProvider(limits),
            minter,
            new WaTelemetryReporter(logger, System.Text.Encoding.UTF8.GetBytes("test-key")),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CreateDraftHandler>.Instance);
        return new Fixture(handler, minter, logger);
    }

    private sealed class StubLimitsProvider(LimitsRecord limits) : ILimitsProvider
    {
        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(limits); // guest always resolves to free here; the handler passes GuestPlanCode.
    }

    /// <summary>Deterministic mint — one URL per (draftId,fileId) so distinct-fileId assertions hold without a live account.</summary>
    private sealed class StubMinter : IBlobSasMinter
    {
        public List<(string DraftId, string FileId)> Mints { get; } = [];

        public Uri MintDraftUpload(string draftId, string fileId)
        {
            Mints.Add((draftId, fileId));
            return new Uri($"http://staging/{draftId}/f/{fileId}?sv=2024&sp=cwr");
        }
    }

    /// <summary>Records the exact rendered text M.E.L hands to a logger — enough to assert the closed event name without a full pipeline (T-056 owns template shape).</summary>
    private sealed class RecordingLogger : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        // Every extension overload funnels into the params overload — one place to record.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Record(state?.ToString() ?? string.Empty);
        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) => Record(message ?? string.Empty);

        private void Record(string renderedText) => Entries.Add(new Entry(renderedText, []));
    }
}
