using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Transfers;

// T-003-04 (D2): the only business logic behind POST /api/v1/public/transfers/{linkId}/unlock.
// Correct password → Active + minted JWT (keying pinned via a known id+hash pair); wrong → result-state WrongPassword
// (not an exception — Program.cs owns the 401 body); terminal rows → their canonical states (D6: status before credentials).
public class UnlockTransferHandlerTests
{
    private readonly StubPublicTransferStore _store;
    private readonly TelemetryRecorder _telemetryLogger;
    private readonly UnlockTransferHandler _handler;

    public UnlockTransferHandlerTests()
    {
        _store = new StubPublicTransferStore();
        _telemetryLogger = new TelemetryRecorder();
        _handler = new UnlockTransferHandler(
            _store,
            new StubUnlockTokenMinter(),
            new WaTelemetryReporter(_telemetryLogger, Encoding.UTF8.GetBytes("unit-test-key")),
            NullLogger<UnlockTransferHandler>.Instance);
    }

    // ---- Correct password → Active + token minted (TA-4.2#5) ----

    [Fact]
    public async Task Handle_CorrectPassword_ReturnsActiveWithMintedToken()
    {
        // Pin the keying: the row's PasswordHash was set with transfer.Id.ToString("n") — exactly SendTransfer's user-id.
        const string password = "hunter2";
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying(password));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", password), CancellationToken.None);

        Assert.Equal(UnlockState.Active, result.State);
        Assert.NotNull(result.Token); // minted — the endpoint returns { token }
        Assert.EndsWith(".abcdefgh", result.Token); // the minter was called with this linkId (StubUnlockTokenMinter echoes it)
        Assert.Null(result.SenderEmail); // only terminal states carry it
    }

    [Fact]
    public async Task Handle_CorrectPassword_EmitsPasswordCorrect_NoWrongEvent()
    {
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        await _handler.Handle(new UnlockTransferCommand("abcdefgh", "hunter2"), CancellationToken.None);

        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.PasswordCorrect);
        Assert.Empty(_telemetryLogger.Entries.Where(e => e.RenderedText == TelemetryEvents.PasswordWrong)); // exactly one of the two closed names
    }

    [Fact]
    public async Task Handle_CorrectPassword_TelemetryNeverCarriesPasswordValue()
    {
        const string password = "hunter2";
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying(password));

        await _handler.Handle(new UnlockTransferCommand("abcdefgh", password), CancellationToken.None);

        // TA-9.4: the password value must never enter any log line (rendered text or args).
        Assert.All(_telemetryLogger.Entries, e =>
        {
            Assert.DoesNotContain(password, e.RenderedText);
            Assert.All(e.Args, arg => Assert.DoesNotContain(password, arg?.ToString()));
        });
    }

    // ---- Wrong password → result-state WrongPassword (D2.6 — not an exception) ----

    [Fact]
    public async Task Handle_WrongPassword_ReturnsWrongPasswordState()
    {
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", "not-the-password"), CancellationToken.None);

        Assert.Equal(UnlockState.WrongPassword, result.State); // Program.cs maps this to the 401 Problem+JSON
        Assert.Null(result.Token); // no token on a wrong password (EC-2)
    }

    [Fact]
    public async Task Handle_WrongPassword_EmitsPasswordWrong_NoCorrectEvent()
    {
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        await _handler.Handle(new UnlockTransferCommand("abcdefgh", "not-the-password"), CancellationToken.None);

        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.PasswordWrong);
        Assert.Empty(_telemetryLogger.Entries.Where(e => e.RenderedText == TelemetryEvents.PasswordCorrect));
    }

    [Fact]
    public async Task Handle_WrongPassword_TelemetryNeverCarriesPasswordValue()
    {
        const string wrongPassword = "definitely-not-it";
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        await _handler.Handle(new UnlockTransferCommand("abcdefgh", wrongPassword), CancellationToken.None);

        // TA-9.4: the wrong-attempted password is PII too — never in telemetry.
        Assert.All(_telemetryLogger.Entries, e =>
        {
            Assert.DoesNotContain(wrongPassword, e.RenderedText);
            Assert.All(e.Args, arg => Assert.DoesNotContain(wrongPassword, arg?.ToString()));
        });
    }

    // ---- Status-before-password (D6 / US-003-05): terminal rows never reach the password logic ----

    [Fact]
    public async Task Handle_StatusExpired_ReturnsExpired_NotWrongPassword()
    {
        _store.Setup(status: 2, passwordHash: HashWithKnownIdKeying("hunter2"), senderEmail: "alice@example.com");

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.Expired, result.State); // status wins over credentials (D6)
        Assert.Equal("alice@example.com", result.SenderEmail); // endpoint renders it into the 410 details
        Assert.Empty(_telemetryLogger.Entries); // no password events on a terminal row — the hash is never consulted
    }

    [Fact]
    public async Task Handle_StatusDownloadLimit_ReturnsDownloadLimit()
    {
        _store.Setup(status: 3, passwordHash: HashWithKnownIdKeying("hunter2"));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.DownloadLimit, result.State); // D2 step 3 mapping
    }

    [Fact]
    public async Task Handle_StatusDeleted_ReturnsNotFound()
    {
        _store.Setup(status: 4, passwordHash: HashWithKnownIdKeying("hunter2"));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.NotFound, result.State); // Status=4 → unknown (FR-003-9 single-body invariant)
    }

    [Fact]
    public async Task Handle_MissingRow_ReturnsNotFound()
    {
        _store.ClearRow(); // no row for this linkId — the store returns null

        var result = await _handler.Handle(new UnlockTransferCommand("zzz99999", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.NotFound, result.State);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task Handle_MalformedLinkId_ReturnsNotFound_EvenWithRow()
    {
        // "abcdefg!" is length 8 with one char outside LinkId.Charset — the stub returns a row (it models a directly-seeded CHAR(8) value),
        // so this exercises the D5 post-query filter rather than the stub-null branch.
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefg!", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.NotFound, result.State); // TA-9.6: no format-error enumeration
    }

    [Fact]
    public async Task Handle_ActiveNoPasswordSet_ReturnsNotFound()
    {
        // D2.4: Active row with PasswordHash == null → NotFound (same body/status as unknown; FR-003-9).
        _store.Setup(status: 1, passwordHash: null);

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", "hunter2"), CancellationToken.None);

        Assert.Equal(UnlockState.NotFound, result.State);
        Assert.Empty(_telemetryLogger.Entries); // no password event — the row isn't gated, so there's nothing to verify
    }

    // ---- Null password → VALIDATION exception BEFORE any DB access (D1) ----

    [Fact]
    public async Task Handle_NullPassword_ThrowsUnlockPasswordMissing_BeforeStoreAccess()
    {
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying("hunter2"));

        await Assert.ThrowsAsync<UnlockPasswordMissingException>(
            () => _handler.Handle(new UnlockTransferCommand("abcdefgh", null), CancellationToken.None));

        Assert.Empty(_telemetryLogger.Entries); // the exception is thrown before any telemetry (D1)
        Assert.False(_store.WasAccessed); // no DB access — the endpoint maps this to VALIDATION with errors[]
    }

    [Fact]
    public async Task Handle_EmptyStringPassword_IsAValue_NotTheMissingException()
    {
        // D1: empty string is a value and validates through — only null throws.
        // A password set to "" at send time is theoretically possible via the same path, so this row is gated on "".
        _store.Setup(status: 1, passwordHash: HashWithKnownIdKeying(""));

        var result = await _handler.Handle(new UnlockTransferCommand("abcdefgh", ""), CancellationToken.None);

        Assert.Equal(UnlockState.Active, result.State); // "" == "" → correct password → Active (same keying as send time)
    }

    // ---- Helpers ----

    /// <summary>Hash with the store's known id using SendTransfer's exact user-id keying (transfer.Id.ToString("n")).</summary>
    private string HashWithKnownIdKeying(string password) =>
        new Microsoft.AspNetCore.Identity.PasswordHasher<string>().HashPassword(_store.KnownId.ToString("n"), password);

    private sealed class StubPublicTransferStore : IPublicTransferStore
    {
        public Transfer? Transfer { get; set; }
        public bool WasAccessed { get; private set; }
        /// <summary>Stable across the test — the handler must verify against this exact id (keying pin: drift breaks Handle_CorrectPassword_ReturnsActiveWithMintedToken).</summary>
        public Guid KnownId { get; } = Guid.NewGuid();

        public void ClearRow() => Transfer = null;

        public Task<(Transfer?, IReadOnlyList<PublicFileItem>)> GetByLinkIdAsync(string linkId, CancellationToken ct)
        {
            WasAccessed = true;
            // The real store returns a row when the LinkId column matches — any 8-char string could theoretically be stored (seeded directly),
            // so this stub models that: return the row for any length-8 linkId. The handler's D5 post-query filter is what keeps malformed IDs unknown.
            if (Transfer is null || linkId.Length != 8)
                return Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((null, []));
            return Task.FromResult<(Transfer?, IReadOnlyList<PublicFileItem>)>((Transfer, []));
        }

        public Task<SingleFileDownloadResult> RecordSingleFileDownloadAsync(Guid transferId, Guid fileId, string? ipHash, string? uaHash, CancellationToken ct) =>
            Task.FromException<SingleFileDownloadResult>(new NotSupportedException("Not used by UnlockTransferHandler (T-003-04); T-003-06's handler tests carry their own stub."));

        public Task<SingleFileDownloadResult> RecordZipDownloadAsync(Guid transferId, string? ipHash, string? uaHash, CancellationToken ct) =>
            Task.FromException<SingleFileDownloadResult>(new NotSupportedException("Not used by UnlockTransferHandler (T-003-04); T-004-02's handler tests carry their own stub."));

        public void Setup(int status, string? passwordHash, string? senderEmail = null) =>
            Transfer = new Transfer
            {
                Id = KnownId,
                LinkId = "abcdefgh",
                Status = (byte)status,
                SenderName = "Alice",
                SenderEmail = senderEmail,
                PasswordHash = passwordHash,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                MaxDownloads = 1,
                DownloadsCount = 0
            };
    }

    /// <summary>Stub for IUnlockTokenMinter — echoes the linkId so tests can assert the mint was called with the right value.</summary>
    private sealed class StubUnlockTokenMinter : IUnlockTokenMinter
    {
        public string Mint(string linkId) => $"stub.jwt.{linkId}";
        public bool Verify(string? token, string expectedSub) => false; // unused in these tests
    }

    /// <summary>Records exactly what M.E.L hands to the logger (same pattern as GetPublicTransferHandlerTests).</summary>
    private sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        // Every extension overload funnels into the params overload — one place to record.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Record(state?.ToString() ?? string.Empty, ExtractArgs(state!));

        public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) =>
            Record(message ?? string.Empty, args);

        private static object?[] ExtractArgs(object state) =>
            ((state as System.Collections.IEnumerable)?
                .Cast<System.Collections.Generic.KeyValuePair<string, object?>>()
                .Where(kv => kv.Key != "{OriginalFormat}")
                .Select(kv => (object?)kv.Value).ToArray()) ?? [];

        private void Record(string renderedText, object?[] args) => Entries.Add(new Entry(renderedText, args));
    }
}
