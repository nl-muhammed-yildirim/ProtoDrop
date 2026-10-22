using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Transfers;

// T-003-06 (TA-4.2#6): the handler is the only business logic behind GET /api/v1/public/transfers/{linkId}/files/{fileId}/download-url?t=.
// Ok path: guard the cap via the store's atomic increment, then mint a 30-minute read SAS with the RFC 5987 disposition (part of the string-to-sign).
// Terminal states + the auth gate map to their canonical result states before any mint (D6 precedence — same as GetPublicTransferHandlerTests).
public class GetFileDownloadUrlHandlerTests
{
    private const string UnitTestKey = "unit-test-key";

    private readonly StubPublicTransferStore _store;
    private readonly StubBlobSasMinter _minter;
    private readonly TelemetryRecorder _telemetryLogger;
    private readonly StubUnlockTokenMinter _tokens;
    private readonly GetFileDownloadUrlHandler _handler;

    public GetFileDownloadUrlHandlerTests()
    {
        _store = new StubPublicTransferStore();
        _minter = new StubBlobSasMinter();
        _telemetryLogger = new TelemetryRecorder();
        _tokens = new StubUnlockTokenMinter();
        _handler = new GetFileDownloadUrlHandler(
            _store,
            _minter,
            _tokens,
            new WaTelemetryReporter(_telemetryLogger, Encoding.UTF8.GetBytes(UnitTestKey)),
            NullLogger<GetFileDownloadUrlHandler>.Instance);
    }

    // ---- Ok path: mint called with the exact RFC 5987 disposition; store recorded with PII-transformed values (TA-9.4) ----

    [Fact]
    public async Task Handle_Ok_MintsSasWithIdNFileIdAndRfc5987Disposition_RecordsStoreWithHashedIpUa()
    {
        var fileId = Guid.NewGuid();
        _store.SetupActive(files: [new PublicFileItem(fileId, "Q4 report (final) v2.pdf", 123)], maxDownloads: 5, downloadsCount: 0);

        var now = DateTimeOffset.UtcNow;
        var result = await _handler.Handle(
            new GetFileDownloadUrlQuery("abcdefgh", fileId.ToString("n"), ClientIp: "1.2.3.4", UserAgent: "ua-xyz"), CancellationToken.None);

        Assert.Equal(FileDownloadState.Ok, result.State);
        // Mint args: transfer id in the canonical "n" form + the file id as given + the RFC 5987 attachment override (EC-003-3).
        Assert.Equal(_store.Transfer!.Id.ToString("n"), _minter.LastTransferId);
        Assert.Equal(fileId.ToString("n"), _minter.LastFileId);
        Assert.Equal($"attachment;filename*=UTF-8''{Uri.EscapeDataString("Q4 report (final) v2.pdf")}", _minter.LastRespContentDisposition);

        // Store: atomic record with the transfer + file ids and the TA-9.4 transforms (HMAC-SHA256 IP keyed by the reporter's key; SHA-256 UA).
        var rec = Assert.Single(_store.Records);
        Assert.Equal(_store.Transfer!.Id, rec.TransferId);
        Assert.Equal(fileId, rec.FileId);
        Assert.Equal(PiiTransforms.IpAddress("1.2.3.4", Encoding.UTF8.GetBytes(UnitTestKey)), rec.IpHash); // IpSlot semantics — non-null when ClientIp given
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ua-xyz"))).ToLowerInvariant(), rec.UaHash); // exactly 64 hex chars

        // ExpiresAt ≈ now + 30 min (the minter's own fixed window — the handler just reports it).
        Assert.NotNull(result.Url);
        Assert.NotNull(result.ExpiresAt);
        Assert.InRange(result.ExpiresAt!.Value - now, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));

        // download_started emitted at mint time (MVP: no client pipeline observes download_completed — documented deviation).
        Assert.Contains(_telemetryLogger.RenderedTexts, e => e == TelemetryEvents.DownloadStarted);
    }

    [Fact]
    public async Task Handle_Ok_NullClientIpAndUserAgent_RecordNullHashes()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0);

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.Ok, result.State);
        // Null stays null — the handler never fabricates hashes (TA-9.4: the raw-empty case is the anonymous case).
        var rec = Assert.Single(_store.Records);
        Assert.Null(rec.IpHash);
        Assert.Null(rec.UaHash);
    }

    // ---- Cap reached → DownloadLimit with SenderEmail; the mint NEVER happens (no side effects on a failed mint) ----

    [Fact]
    public async Task Handle_CapReached_ReturnsDownloadLimit_MintNotCalled()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 1, downloadsCount: 1); // at the cap
        _store.RecordOutcome = SingleFileDownloadOutcome.CapReached;

        var result = await _handler.Handle(
            new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n"), ClientIp: "1.2.3.4"), CancellationToken.None);

        Assert.Equal(FileDownloadState.DownloadLimit, result.State);
        Assert.Null(result.Url);
        Assert.Null(result.ExpiresAt);
        Assert.Equal("alice@example.com", result.SenderEmail); // endpoint renders it into Problem+JSON details (FR-003-8)
        Assert.Empty(_minter.Calls); // no mint on the cap path — a rejected mint must have zero side effects
    }

    // ---- Terminal states + unknown lookups: canonical states, SenderEmail only on terminal-with-email ----

    [Fact]
    public async Task Handle_Status2_ReturnsExpired_SenderEmailPassedThrough()
    {
        _store.SetupStatus(status: 2, senderEmail: "alice@example.com");

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.Expired, result.State); // the 410 expired body — the endpoint's Expired branch
        Assert.Equal("alice@example.com", result.SenderEmail);
        Assert.Empty(_minter.Calls);
        Assert.Empty(_store.Records); // no download counted on a terminal row
    }

    [Fact]
    public async Task Handle_Status3_ReturnsDownloadLimit_SenderEmailPassedThrough()
    {
        _store.SetupStatus(status: 3, senderEmail: "alice@example.com");

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.DownloadLimit, result.State); // the 410 MAX_DOWNLOADS_REACHED body — the endpoint's DownloadLimit branch
        Assert.Equal("alice@example.com", result.SenderEmail);
        Assert.Empty(_minter.Calls);
    }

    [Fact]
    public async Task Handle_UnknownLinkId_ReturnsNotFound_SenderEmailNull()
    {
        _store.Transfer = null;

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.NotFound, result.State);
        Assert.Null(result.SenderEmail); // identical Problem+JSON body to unknown linkId (FR-003-9)
    }

    [Fact]
    public async Task Handle_Status4_ReturnsNotFound_SenderEmailNull()
    {
        _store.SetupStatus(status: 4); // DECISION A: f-delete-transfers → hard-deleted FileItems + Status=4 = indistinguishable unknown

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.NotFound, result.State);
        Assert.Null(result.SenderEmail); // no byte leak — no file list shape for a hard-deleted transfer
    }

    [Fact]
    public async Task Handle_MalformedLinkId_WrongLength_ReturnsNotFound_EvenWithRow()
    {
        _store.SetupActive(files: [], maxDownloads: 5, downloadsCount: 0); // D5 filter: a seeded row never bypasses the well-formed check

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("short", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.NotFound, result.State);
        Assert.Empty(_minter.Calls);
    }

    [Fact]
    public async Task Handle_MalformedFileId_ReturnsNotFound_NoEnumeration()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0);

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", "not-a-guid"), CancellationToken.None);

        Assert.Equal(FileDownloadState.NotFound, result.State); // TA-9.6: no format-error leak (no 400, no distinct body)
        Assert.Null(result.SenderEmail);
        Assert.Empty(_minter.Calls);
    }

    [Fact]
    public async Task Handle_UnknownFileId_ReturnsNotFound_SenderEmailNull()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0);

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None); // a well-formed id not in this transfer

        Assert.Equal(FileDownloadState.NotFound, result.State);
        Assert.Null(result.SenderEmail); // same body as unknown linkId — no fileId enumeration (TA-9.6)
    }

    [Fact]
    public async Task Handle_FileIdWithDifferentCase_ReturnsOk_SameFile()
    {
        var fileId = Guid.NewGuid();
        _store.SetupActive(files: [new PublicFileItem(fileId, "a.txt", 10)], maxDownloads: 5, downloadsCount: 0);

        var upper = fileId.ToString().ToUpperInvariant(); // well-formed Guid.Parse-able, different from the "n" form on disk

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", upper), CancellationToken.None);

        Assert.Equal(FileDownloadState.Ok, result.State); // HasFile matches case-insensitively — no false NotFound
        Assert.Equal(fileId.ToString("n"), _minter.LastFileId);
    }

    // ---- Auth gate: gated transfer + ?t= (T-003-04 reuse) → Unauthenticated vs Ok ----

    [Fact]
    public async Task Handle_Gated_NoToken_ReturnsUnauthenticated()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0, passwordHash: "hash");

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.Unauthenticated, result.State); // the 401 UNAUTHENTICATED body — never a file-list-shaped error
        Assert.Empty(_minter.Calls);
    }

    [Fact]
    public async Task Handle_Gated_InvalidToken_ReturnsUnauthenticated()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0, passwordHash: "hash");
        _tokens.VerifyResult = false;

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n"), Token: "garbage"), CancellationToken.None);

        Assert.Equal(FileDownloadState.Unauthenticated, result.State); // junk token behaves exactly like an absent one (TA-9.6)
    }

    [Fact]
    public async Task Handle_Gated_ValidToken_ReturnsOk()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0, passwordHash: "hash");
        _tokens.VerifyResult = true;

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n"), Token: "valid.jwt.token"), CancellationToken.None);

        Assert.Equal(FileDownloadState.Ok, result.State); // a valid ?t= unlocks the mint (AC-003-2 server half)
        Assert.NotNull(result.Url);
    }

    [Fact]
    public async Task Handle_Ungated_AnyToken_ReturnsOk()
    {
        _store.SetupActive(files: [new PublicFileItem(Guid.NewGuid(), "a.txt", 10)], maxDownloads: 5, downloadsCount: 0, passwordHash: null);

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", _store.Files![0].FileId.ToString("n"), Token: "whatever"), CancellationToken.None);

        Assert.Equal(FileDownloadState.Ok, result.State); // ungated — the token is irrelevant
    }

    // ---- D6 precedence: terminal rows beat the auth gate (same precedent as GetPublicTransferHandlerTests for endpoint 4) ----

    [Fact]
    public async Task Handle_Status3_Gated_NoToken_ReturnsDownloadLimit_TerminalBeatsAuthGate()
    {
        _store.SetupStatus(status: 3, senderEmail: "alice@example.com"); // terminal row — the status check runs BEFORE any token logic
        // No token given (the gated-row precondition).

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n")), CancellationToken.None);

        Assert.Equal(FileDownloadState.DownloadLimit, result.State); // DownloadLimit wins — never 401 UNAUTHENTICATED on a terminal row
        Assert.Equal("alice@example.com", result.SenderEmail);
    }

    [Fact]
    public async Task Handle_Status2_Gated_ValidToken_ReturnsExpired_TerminalBeatsAuthGate()
    {
        _store.SetupStatus(status: 2, senderEmail: "alice@example.com");
        _tokens.VerifyResult = true; // a valid token would clear the gate — but Expired wins (US-003-05: earlier of the two)

        var result = await _handler.Handle(new GetFileDownloadUrlQuery("abcdefgh", Guid.NewGuid().ToString("n"), Token: "valid.jwt.token"), CancellationToken.None);

        Assert.Equal(FileDownloadState.Expired, result.State);
        Assert.Equal("alice@example.com", result.SenderEmail);
    }

    // ---- Stubs (same fake style as GetPublicTransferHandlerTests) ----

    private sealed class StubPublicTransferStore : IPublicTransferStore
    {
        public Transfer? Transfer { get; set; }
        public IReadOnlyList<PublicFileItem>? Files { get; set; }

        /// <summary>The outcome RecordSingleFileDownloadAsync returns — tests flip it to CapReached for the cap path.</summary>
        public SingleFileDownloadOutcome RecordOutcome { get; set; } = SingleFileDownloadOutcome.Recorded;

        /// <summary>The (transferId, fileId, ipHash, uaHash) tuples the handler passed to RecordSingleFileDownloadAsync — asserted, not faked.</summary>
        public List<(Guid TransferId, Guid FileId, string? IpHash, string? UaHash)> Records { get; } = [];

        public Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct) =>
            linkId.Length == 8 && Transfer is not null
                ? Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((Transfer, Files ?? []))
                : Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((null, []));

        public Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(Guid transferId, Guid fileId, string? ipHash, string? uaHash, CancellationToken ct)
        {
            Records.Add((transferId, fileId, ipHash, uaHash));
            return Task.FromResult(new SingleFileDownloadResult(RecordOutcome));
        }

        public Task<SingleFileDownloadResult> RecordZipDownloadAsync(Guid transferId, string? ipHash, string? uaHash, CancellationToken ct) =>
            Task.FromResult(new SingleFileDownloadResult(RecordOutcome)); // not called by this handler (T-004-02 is a separate endpoint)

        public void SetupActive(IReadOnlyList<PublicFileItem> files, int maxDownloads, int downloadsCount, string? passwordHash = null) =>
            SetupStatus(status: 1, senderEmail: "alice@example.com", files: files, maxDownloads: maxDownloads, downloadsCount: downloadsCount, passwordHash: passwordHash);

        public void SetupStatus(int status, string? senderEmail = null, IReadOnlyList<PublicFileItem>? files = null, int maxDownloads = -1, int downloadsCount = 0, string? passwordHash = null)
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
                TotalBytes = files?.Sum(f => f.SizeBytes) ?? 0,
                FileCount = files?.Count ?? 0,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            };
        }
    }

    private sealed class StubBlobSasMinter : IBlobSasMinter
    {
        public List<(string TransferId, string FileId, string? RespContentDisposition)> Calls { get; } = [];
        public string? LastTransferId => Calls[^1].TransferId;
        public string? LastFileId => Calls[^1].FileId;
        public string? LastRespContentDisposition => Calls[^1].RespContentDisposition;

        public Uri MintDraftUpload(string draftId, string fileId) => new("https://draft.local/upload"); // not called by this handler (T-003-06 is read-only)

        public Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition)
        {
            Calls.Add((transferId, fileId, respContentDisposition));
            return new Uri("https://blob.example/sas?sig=test"); // the Ok-path URL — asserted non-null
        }

        public Uri MintRecipientZip(string transferId) => new("https://blob.example/zip-sas?sig=test"); // not called by this handler (T-004-02 is a separate endpoint)
    }

    private sealed class StubUnlockTokenMinter : IUnlockTokenMinter
    {
        public bool VerifyResult { get; set; } = false;

        public string Mint(string linkId) => $"stub.jwt.{linkId}"; // never called by this handler (no unlock minting here)

        public bool Verify(string? token, string expectedSub) => VerifyResult; // tests control the valid/junk outcome directly
    }

    /// <summary>Records exactly what M.E.L hands to the logger: rendered text + pre-formatted args (same style as WaTelemetryReporterTests).</summary>
    internal sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public List<string> RenderedTexts { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        // Every extension overload funnels into the params overload — one place to record.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => RenderedTexts.Add(state?.ToString() ?? string.Empty);
        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) => RenderedTexts.Add(message ?? string.Empty);
    }
}
