using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Transfers;

// T-001-05: the handler is the only business logic behind POST /api/v1/transfers/draft (TA-4.2a). Free guest limits are resolved via ILimitsProvider — never literals — and the whole selection is checked against MAX_TRANSFER_SIZE + per-file MAX_SINGLE_FILE before minting one staging SAS per file. Throws TransferSizeExceededException when oversized; upload_started emits exactly once on success.
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

        Assert.Equal(7200, result.ExpiresInSec); // TA-3.6's 2 h draft-upload TTL — constant, not a plan limit.
        Assert.Equal(files.Length, result.Files.Count);
        Assert.Equal(result.Files.Count, result.Files.Select(x => x.FileId).Distinct().Count()); // one mint per file, all distinct (FR: fresh fileId per staged file).
        Assert.Equal(files.Length, f.Minter.Mints.Count);
        foreach (var (mintDraft, _) in f.Minter.Mints)
            Assert.Equal(result.DraftId.ToString("n"), mintDraft); // every mint is addressed to the same draft.
    }

    [Fact]
    public async Task Handle_TotalExceedsMaxTransferSize_ThrowsWithBothNumbers() // TA-4.1.3 TRANSFER_SIZE_EXCEEDED path — endpoint maps exception → 422.
    {
        var f = CreateFixture(FreeLimits());

        var ex = await Assert.ThrowsAsync<TransferSizeExceededException>(async () =>
            await f.Handler.Handle(new CreateDraftCommand([
                new CreateDraftFile { Name = "big.mp4", SizeBytes = FreeMax },
                new CreateDraftFile { Name = "one-byte.bin", SizeBytes = 1 }]), CancellationToken.None)); // total = MaxTransferSize + 1.

        Assert.Equal(FreeMax + 1, ex.TotalBytes);
        Assert.Equal(FreeMax, ex.MaxTransferSize);
        Assert.Equal(0, ex.SingleFileBytes); // total violation — no single-file bytes carried.
        Assert.Contains("5.0 GB", ex.Message); // both numbers present in the details (AC-007-1).
    }

    [Fact]
    public async Task Handle_SingleFileExceedsMaxSingleFile_Throws()
    {
        var f = CreateFixture(FreeLimits());

        var ex = await Assert.ThrowsAsync<TransferSizeExceededException>(async () =>
            await f.Handler.Handle(new CreateDraftCommand([
                new CreateDraftFile { Name = "huge.bin", SizeBytes = FreeMax + 1 }]), CancellationToken.None)); // total also exceeds, but the per-file check is what this pins.

        Assert.Equal(FreeMax + 1, ex.SingleFileBytes);
        Assert.Equal(FreeMax, ex.MaxSingleFile);
        Assert.Contains("File size", ex.Message); // single-file violation wording.
    }

    [Fact]
    public async Task Handle_SingleFileOver_TotalFits_ThrowsSingleFileViolation() // true per-file violation: MaxSingleFile < MaxTransferSize.
    {
        var f = CreateFixture(new LimitsRecord { MaxTransferSize = 5000, MaxSingleFile = 1000 });

        var ex = await Assert.ThrowsAsync<TransferSizeExceededException>(async () =>
            await f.Handler.Handle(new CreateDraftCommand([
                new CreateDraftFile { Name = "a.bin", SizeBytes = 600 },
                new CreateDraftFile { Name = "b.bin", SizeBytes = 1001 }]), CancellationToken.None)); // total = 1601 < 5000, but b.bin > 1000.

        Assert.Equal(1001, ex.SingleFileBytes);
        Assert.Equal(1000, ex.MaxSingleFile);
        Assert.Equal(1601, ex.TotalBytes); // total is carried for context even when the single-file check fires.
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

        await Assert.ThrowsAsync<TransferSizeExceededException>(async () =>
            await f.Handler.Handle(new CreateDraftCommand([new CreateDraftFile { Name = "big.bin", SizeBytes = FreeMax + 1 }]), CancellationToken.None));

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

        // T-003-06: the interface gained the 3-arg recipient mint — unused by CreateDraft (endpoint 1), kept for completeness.
        public Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition)
            => new Uri($"http://transfers/{transferId}/files/{fileId}?sv=2024&sp=r&rs-content-disposition={Uri.EscapeDataString(respContentDisposition)}");

        // T-004-01: the interface gained the zip mint — unused by CreateDraft (endpoint 1), kept for completeness.
        public Uri MintRecipientZip(string transferId)
            => new Uri($"http://transfers/{transferId}/all.zip?sv=2024&sp=r");
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
