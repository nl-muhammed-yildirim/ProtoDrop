using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Auth;

// T-008-01 (TA-4.2#8): the handler is the only business logic behind POST /api/v1/auth/signup (TA-4.2a).
// Sign-up is sign-in (FR-008-3): unknown email → create row + user_created once; known email (any case) → existing row + login_success on both branches.
public class SignupHandlerTests
{
    private readonly StubAppUserStore _store;
    private readonly TelemetryRecorder _telemetryLogger;
    private readonly StubPlansStore _plans;
    private readonly SessionTokenService _minter;
    private readonly Plan _freePlan;
    private readonly SignupHandler _handler;

    public SignupHandlerTests()
    {
        _store = new StubAppUserStore();
        _telemetryLogger = new TelemetryRecorder();
        _plans = new StubPlansStore();
        _minter = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("unit-test-session-key"), () => DateTimeOffset.UtcNow);
        _freePlan = _plans.Plan;
        _handler = new SignupHandler(
            _store,
            _plans,
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            _minter,
            NullLogger<SignupHandler>.Instance);
    }

    // ---- AC-008-1: new email → row created, user_created emitted once, session token returned ----

    [Fact]
    public async Task Signup_CreatesUser_Lowercased_HashesPassword_EmitsUserCreatedOnce_AndReturnsToken()
    {
        var result = await _handler.Handle(new SignupCommand("John.Doe@Example.COM", "password123"), CancellationToken.None);

        Assert.Single(_store.Created); // AC-008-1: exactly one row on unknown email
        AppUser? created = _store.Created[0];
        Assert.Equal("john.doe@example.com", created.Email!); // EC-008-1: lowercased + trimmed

        // TA-9.1: PBKDF2-SHA256 via ASP.NET Identity PasswordHasher<string>, keyed by userId.ToString("n").
        var hasher = new PasswordHasher<string>();
        var verifyStatus = hasher.VerifyHashedPassword(created.Id.ToString("n"), created.PasswordHash!, "password123");
        Assert.True(verifyStatus is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded);

        // AC-008-1: user_created emitted exactly once on create branch.
        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.UserCreated);

        // US-008-01 edge case: login_success emitted on both branches (even the create path).
        Assert.Contains(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginSuccess);

        Assert.NotEmpty(result.SessionToken); // TA-9.1 session token in result
        Assert.Equal("john.doe@example.com", result.Email);
        Assert.Equal("john.doe", result.DisplayName); // FR-008-4: local part of email
        Assert.Equal(_freePlan.Code, result.PlanCode);
    }

    [Fact]
    public async Task Signup_NormalizesEmail_ToLowercaseBeforeSaveAndLookup()
    {
        // EC-008-1: normalization — lowercase + trim before lookup and save.
        var result = await _handler.Handle(new SignupCommand("  Test@EXAMPLE.com  ", "password123"), CancellationToken.None);

        Assert.Single(_store.Created);
        Assert.Equal("test@example.com", _store.Created[0].Email!); // trimmed + lowercased
        Assert.Equal("test@example.com", result.Email);
        Assert.NotEmpty(result.SessionToken);
    }

    [Fact]
    public async Task Signup_KnownEmail_DifferentCase_FindsSameRow_NoDuplicate()
    {
        // EC-008-4: sign-up is sign-in — known email (any case) signs into existing row; no duplicate insert.
        var existingId = Guid.NewGuid();
        _store.ExistingUser = new AppUser
        {
            Id = existingId,
            Email = "existing@proto.drop",
            DisplayName = "Existing",
            Theme = "system",
            PasswordHash = "stored-hash",
            PlanId = _freePlan.Id,
            EmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _handler.Handle(new SignupCommand("Existing@PROTO.drop", "password123"), CancellationToken.None);

        Assert.Empty(_store.Created); // no insert on known-email branch (EC-008-4)
        Assert.Equal(existingId, result.UserId);
        Assert.Equal("existing@proto.drop", result.Email);
        Assert.NotEmpty(result.SessionToken);

        // US-008-01: login_success emitted on both branches.
        Assert.Contains(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginSuccess);
    }

    [Fact]
    public async Task Signup_PasswordUnderEight_ThrowsValidation()
    {
        // FR-008-5: server-side re-check — password must be at least 8 characters.
        await Assert.ThrowsAsync<SignupValidationException>(async () =>
            await _handler.Handle(new SignupCommand("short@proto.drop", "1234567"), CancellationToken.None));

        Assert.Empty(_store.Created); // no row created on validation failure
    }

    [Fact]
    public async Task Signup_BadEmailFormat_ThrowsValidation()
    {
        // TA-4.2#8: invalid email format → VALIDATION (no new problem code).
        await Assert.ThrowsAsync<SignupValidationException>(async () =>
            await _handler.Handle(new SignupCommand("not-an-email", "password123"), CancellationToken.None));

        Assert.Empty(_store.Created); // no row created on validation failure
    }

    // ---- Stubs (T-014 style — hand-written stubs, no Moq) ----

    private sealed class StubAppUserStore : IAppUserStore
    {
        public List<AppUser> Created { get; } = [];
        public AppUser? ExistingUser { get; set; }

        // EC-008-4: the store models a real DB — only returns a row when the stored (lowercased) email matches exactly.
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
            Task.FromResult(ExistingUser?.Email == lowercasedEmail ? ExistingUser : null);

        public Task CreateAsync(AppUser user, CancellationToken ct)
        {
            Created.Add(user);
            return Task.CompletedTask;
        }

        // T-008-06 (D-1): compile fix only — the Signup handler doesn't call these new members.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(ExistingUser?.Id == userId ? ExistingUser : null);
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubPlansStore : IPlansStore
    {
        // FR-004: "Free by default" is a seeded row — test stub mirrors the seed (TA-3.4).
        public Plan Plan { get; } = new() { Id = Guid.NewGuid(), Code = "free", Name = "Free" };

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            planCode == "free" ? Task.FromResult(Plan) : throw new PlanUnknownException(planCode);

        // T-008-02: by-Id read (returns null or the row; never throws) — new IPlansStore member from the plan.
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plan.Id == planId ? Plan : null);
    }

    /// <summary>T-014 style: records exactly what M.E.L hands to the logger (same as WaTelemetryReporterTests).</summary>
    internal sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        // The extension overloads funnel into the params overload — one place to record.
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
