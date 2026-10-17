using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Limits;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Transfers;

// T-014 (TA-4.2#4): the handler is the only business logic behind GET /api/v1/public/transfers/{linkId} (TA-4.2a).
// Active → full metadata payload (no SAS minted, no download consumed); terminal states → minimal result, no file rows.
public class GetPublicTransferHandlerTests
{
    private readonly StubPublicTransferStore _store;
    private readonly TelemetryRecorder _telemetryLogger;
    private readonly GetPublicTransferHandler _handler;

    public GetPublicTransferHandlerTests()
    {
        _store = new StubPublicTransferStore();
        _telemetryLogger = new TelemetryRecorder();
        _handler = new GetPublicTransferHandler(
            _store,
            NewLimits(maxZipSize: 4294967296), // free-plan seeded values (TA-3.4: no literals in handler code — the stub is test data)
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            NullLogger<GetPublicTransferHandler>.Instance);
    }

    // ---- Active: full metadata payload (exit check 3 / FR-003-5) ----

    [Fact]
    public async Task Handle_Active_FiniteCap_ReturnsFilesDownloadsLeftPasswordRequired()
    {
        var firstFile = new PublicFileItem(Guid.NewGuid(), "a.txt", 100);
        var secondFile = new PublicFileItem(Guid.NewGuid(), "b.txt", 200);
        _store.SetupActive(files: [firstFile, secondFile], maxDownloads: 100, downloadsCount: 13, passwordHash: "hash");

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.Active, result.State);
        Assert.Equal("Alice", result.From); // SenderName pass-through
        Assert.Equal("Hello there", result.Note);
        Assert.Equal([firstFile, secondFile], result.Files!.ToArray()); // file rows pass through in store order (adapter orders by SortOrder)
        // FileCount=2 and TotalBytes=300 ≤ MaxZipSize → true — the test comment was wrong, but the logic is correct
        Assert.True(result.HasDownloadAll); // FileCount ≥ 2 AND TotalBytes ≤ MaxZipSize
        Assert.Equal(87, result.DownloadsLeft); // 100 - 13 — same MaxDownloads/DownloadsCount columns TA-7.2's flip uses
        Assert.True(result.PasswordRequired);
    }

    [Fact]
    public async Task Handle_Active_UnlimitedCap_DownloadsLeftNull()
    {
        _store.SetupActive(files: [], maxDownloads: -1, downloadsCount: 0, passwordHash: null); // MaxDownloads=-1 → Business (∞)

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.Active, result.State);
        Assert.Null(result.DownloadsLeft); // FR-003-5: "Downloads left" hidden for infinite caps — JSON null on the wire
        Assert.False(result.PasswordRequired);
    }

    [Fact]
    public async Task Handle_Active_HasDownloadAll_FalseWhenSingleFile()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 100)], maxDownloads: 1, downloadsCount: 0, passwordHash: null);

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.False(result.HasDownloadAll); // <2 files → false even under the zip limit
    }

    [Fact]
    public async Task Handle_Active_HasDownloadAll_FalseWhenTotalExceedsMaxZipSize()
    {
        _store.SetupActive(
            files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 2500000000), new PublicFileItem(Guid.NewGuid(), "b.txt", 2500000000)],
            maxDownloads: 1, downloadsCount: 0, passwordHash: null); // 5 GB > the stub's MaxZipSize (4294967296)

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.False(result.HasDownloadAll); // total > MAX_ZIP_SIZE via ILimitsProvider — no literal in the handler
    }

    [Fact]
    public async Task Handle_Active_HasDownloadAll_TrueWhenTwoFilesUnderZipLimit()
    {
        _store.SetupActive(
            files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 100), new PublicFileItem(Guid.NewGuid(), "b.txt", 200)],
            maxDownloads: 1, downloadsCount: 0, passwordHash: null);

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.True(result.HasDownloadAll); // ≥2 files AND total ≤ MaxZipSize
    }

    // ---- Terminal states: minimal result, no file rows (AC-003-3, no byte leak) ----

    [Fact]
    public async Task Handle_Expired_ReturnsExpired_NoFiles_SenderEmailPassedThrough()
    {
        _store.SetupStatus(status: 2, senderEmail: "alice@example.com");

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.Expired, result.State);
        Assert.Null(result.Files); // no file list on the wire (AC-003-3)
        Assert.Null(result.From);
        Assert.Null(result.Note);
        Assert.Null(result.DownloadsLeft);
        Assert.False(result.HasDownloadAll);
        Assert.Equal("alice@example.com", result.SenderEmail); // endpoint renders it into Problem+JSON details
    }

    [Fact]
    public async Task Handle_DownloadLimit_ReturnsDownloadLimit_NoFiles()
    {
        _store.SetupStatus(status: 3, senderEmail: null);

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.DownloadLimit, result.State);
        Assert.Null(result.Files);
        Assert.Null(result.SenderEmail);
    }

    [Fact]
    public async Task Handle_UnknownLinkId_ReturnsNotFound_SenderEmailNull()
    {
        _store.Transfer = null; // no row

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.NotFound, result.State);
        Assert.Null(result.SenderEmail); // identical Problem+JSON body to expired-with-no-email (FR-003-9)
        Assert.Null(result.Files);
    }

    [Fact]
    public async Task Handle_MalformedLinkId_WrongLengthOrCharset_ReturnsNotFound_EvenWithRow()
    {
        _store.SetupActive(files: [], maxDownloads: 1, downloadsCount: 0, passwordHash: null); // row would exist for any linkId

        var shortResult = await _handler.Handle(new GetPublicTransferQuery("short"), CancellationToken.None);
        Assert.Equal(PublicTransferState.NotFound, shortResult.State);

        var outOfCharsetResult = await _handler.Handle(new GetPublicTransferQuery("ABCDEFGH!"), CancellationToken.None); // 8 chars, outside the LinkId charset (D5)
        Assert.Equal(PublicTransferState.NotFound, outOfCharsetResult.State);
    }

    [Fact]
    public async Task Handle_DeletedStatus_ReturnsNotFound()
    {
        _store.SetupStatus(status: 4); // f-delete-transfers (TA-6.4): hard-deletes FileItems + Status=4 → indistinguishable unknown (DECISION A)

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(PublicTransferState.NotFound, result.State);
        Assert.Null(result.Files); // no byte leak even though the row exists
    }

    // ---- Telemetry (TA-4.2#4 emits column; AC-003-4) ----

    [Fact]
    public async Task Handle_TransferPageViewed_EmittedOnceWithLinkIdHash_NotTheRawId()
    {
        _store.SetupActive(files: [], maxDownloads: 1, downloadsCount: 0, passwordHash: null);

        await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        var pageViewed = Assert.Single(_telemetryLogger.Entries, e => e.RenderedText.StartsWith(TelemetryEvents.TransferPageViewed));
        // linkId hash = lowercase SHA-256 of the raw id (TA-9.4 style) — never the raw 8-char id
        Assert.Equal("9c56cc51b374c3ba189210d5b6d4bf57790d351c96c47c02190ecf1e430635ab", pageViewed.Args[0]?.ToString());
        Assert.DoesNotContain("abcdefgh", pageViewed.RenderedText);
    }

    [Fact]
    public async Task Handle_TransferNotFound_EmittedOnlyOnTheNotFoundBranch()
    {
        _store.Transfer = null;

        await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Contains(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.TransferPageViewed || e.RenderedText.StartsWith(TelemetryEvents.TransferPageViewed));
        var notFound = Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.TransferNotFound);
        Assert.Empty(notFound.Args); // property-free — the 404 case means no row to hash against (D4)
    }

    [Theory]
    [InlineData(1, PublicTransferState.Active)]
    [InlineData(2, PublicTransferState.Expired)]
    [InlineData(3, PublicTransferState.DownloadLimit)]
    [InlineData(4, PublicTransferState.NotFound)] // D3/DECISION A: Status=4 → NotFound (transfer_not_found included)
    public async Task Handle_EachState_EmitsPageViewedAndOnlyNotFoundEmitsTransferNotFound(int status, PublicTransferState expectedState)
    {
        _store.SetupStatus(status: status);

        var result = await _handler.Handle(new GetPublicTransferQuery("abcdefgh"), CancellationToken.None);

        Assert.Equal(expectedState, result.State);
        // transfer_page_viewed on every load (TA-4.2#4 emits column) — even terminal states (US-003-5 technical notes).
        Assert.Contains(_telemetryLogger.Entries, e => e.RenderedText.StartsWith(TelemetryEvents.TransferPageViewed));
        var emittedNotFound = _telemetryLogger.Entries.Any(e => e.RenderedText == TelemetryEvents.TransferNotFound);
        Assert.Equal(status == 4, emittedNotFound); // transfer_not_found only on the NotFound branch — never for Active/Expired/DownloadLimit
    }

    // ---- Stubs ----

    private sealed class StubPublicTransferStore : IPublicTransferStore
    {
        public Transfer? Transfer { get; set; }
        public IReadOnlyList<PublicFileItem>? Files { get; set; }

        public Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct)
        {
            // The real store returns a row when the LinkId column matches — any 8-char string could theoretically be stored
            // (seeded directly), so this stub models that: return the row for any length-8 linkId. The handler's D5
            // pre-filter (well-formed charset check) is what keeps malformed IDs on the unknown branch — testable here.
            if (Transfer is null || linkId.Length != 8)
                return Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((null, []));
            return Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((Transfer, Files ?? []));
        }

        public void SetupActive(IReadOnlyList<PublicFileItem> files, int maxDownloads, int downloadsCount, string? passwordHash) =>
            SetupStatus(status: 1, senderEmail: "alice@example.com", files: files, maxDownloads: maxDownloads, downloadsCount: downloadsCount, passwordHash: passwordHash);

        public void SetupStatus(int status, string? senderEmail = null, IReadOnlyList<PublicFileItem>? files = null, int maxDownloads = -1, int downloadsCount = 0, string? passwordHash = null, long? totalBytesOverride = null, int? fileCountOverride = null)
        {
            Files = files;
            Transfer = new Transfer
            {
                Id = Guid.NewGuid(),
                LinkId = "abcdefgh",
                Status = (byte)status,
                SenderName = "Alice",
                SenderEmail = senderEmail,
                Note = "Hello there",
                MaxDownloads = maxDownloads,
                DownloadsCount = downloadsCount,
                PasswordHash = passwordHash,
                TotalBytes = totalBytesOverride ?? (files?.Sum(f => f.SizeBytes) ?? 0),
                FileCount = fileCountOverride ?? (files?.Count ?? 0),
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            };
        }
    }

    private static ILimitsProvider NewLimits(long maxZipSize) => new StubLimitsProvider(maxZipSize);

    private sealed class StubLimitsProvider : ILimitsProvider
    {
        public long MaxZipSize { get; }

        public StubLimitsProvider(long maxZipSize) => MaxZipSize = maxZipSize;

        public Task<LimitsRecord> Resolve(string planCode, CancellationToken cancellationToken) =>
            Task.FromResult(new LimitsRecord { MaxZipSize = MaxZipSize });
    }

    /// <summary>Records exactly what M.E.L hands to the logger: rendered text + pre-formatted args (same style as WaTelemetryReporterTests).</summary>
    internal sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        // Every extension overload funnels into the params overload — one place to record.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Record(state?.ToString() ?? string.Empty, ExtractArgs(state!));
        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) => Record(message ?? string.Empty, args);

        private static object?[] ExtractArgs(object state) =>
            ((state as System.Collections.IEnumerable)?
                .Cast<System.Collections.Generic.KeyValuePair<string, object?>>()
                .Where(kv => kv.Key != "{OriginalFormat}")
                .Select(kv => (object?)kv.Value).ToArray()) ?? [];

        private void Record(string renderedText, object?[] args) => Entries.Add(new Entry(renderedText, args));
    }
}
