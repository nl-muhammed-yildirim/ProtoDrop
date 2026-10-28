using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.unit.Auth;

// T-008-02 (TA-4.2#9 / FR-008-1): LoginHandler is the only business logic behind POST /api/v1/auth/login (TA-4.2a).
// No row OR failed hash verify → LoginWrongPasswordException (never "user not found" — don't leak which emails exist).
// Validation failures → LoginValidationException (endpoint maps to VALIDATION 400).
public class LoginHandlerTests
{
    private readonly StubAppUserStore _users;
    private readonly TelemetryRecorder _telemetryLogger;
    private readonly StubPlansStore _plans;
    private readonly SessionTokenService _minter;
    private readonly Plan _freePlan;
    private readonly LoginHandler _handler;

    public LoginHandlerTests()
    {
        _users = new StubAppUserStore();
        _telemetryLogger = new TelemetryRecorder();
        _plans = new StubPlansStore();
        _minter = new SessionTokenService(System.Text.Encoding.UTF8.GetBytes("unit-test-session-key"), () => DateTimeOffset.UtcNow);
        _freePlan = _plans.Plan;
        _handler = new LoginHandler(
            _users,
            _plans,
            new WaTelemetryReporter(_telemetryLogger, System.Text.Encoding.UTF8.GetBytes("unit-test-key")),
            _minter);
    }

    private AppUser SeedRealAppUser(Guid id) =>
        new()
        {
            Id = id,
            Email = "real@example.com",
            DisplayName = "real",
            Theme = "system",
            PasswordHash = new PasswordHasher<string>().HashPassword(id.ToString("n"), "password123"),
            PlanId = _freePlan.Id,
            EmailConfirmed = false,
            CreatedAtUtc = DateTime.UtcNow
        };

    // ---- AC-008-1 login half: correct password → LoginResult with session token, login_success emitted once ----

    [Fact]
    public async Task Login_CorrectPassword_ReturnsLoginResult_EmitsLoginSuccessOnce()
    {
        var userId = Guid.NewGuid();
        _users.ExistingUser = SeedRealAppUser(userId);

        var result = await _handler.Handle(new LoginCommand("real@example.com", "password123"), CancellationToken.None);

        Assert.Equal(userId, result.UserId);
        Assert.Equal("real@example.com", result.Email);
        Assert.NotEmpty(result.SessionToken); // TA-9.1 session token in result

        // AC-008-1: login_success emitted exactly once on success.
        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginSuccess);
    }

    // ---- EC-008-1: email with different case signs in to the same row — login never creates a user ----

    [Fact]
    public async Task Login_DifferentCaseEmail_ResolvesSameRow_NeverCreatesUser()
    {
        var userId = Guid.NewGuid();
        _users.ExistingUser = SeedRealAppUser(userId);

        // EC-008-1: normalization — lowercase + trim before lookup (same form as SignupHandler.NormalizeEmail).
        var result = await _handler.Handle(new LoginCommand("  Real@EXAMPLE.com ", "password123"), CancellationToken.None);

        Assert.Empty(_users.Created); // login NEVER creates a row (no insert on the login path)
        Assert.Equal(userId, result.UserId);
    }

    // ---- Wrong password → login_failed emitted; single exception shape ----

    [Fact]
    public async Task Login_WrongPassword_ThrowsWrongPassword_EmitsLoginFailedOnce()
    {
        var userId = Guid.NewGuid();
        _users.ExistingUser = SeedRealAppUser(userId);

        await Assert.ThrowsAsync<LoginWrongPasswordException>(async () =>
            await _handler.Handle(new LoginCommand("real@example.com", "wrong-pass-9"), CancellationToken.None));

        // AC-008-1 / task §4: login_failed emitted exactly once on the wrong-password path.
        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginFailed);
        Assert.Empty(_users.Created); // no row created on auth failure
    }

    // ---- EC-008-4 (no-leak): unknown email → same exception as wrong password, NOT "user not found" ----

    [Fact]
    public async Task NoRow_ReturnsWrongPassword_NotNotFound()
    {
        _users.ExistingUser = null; // no row for this email

        var ex = await Assert.ThrowsAsync<LoginWrongPasswordException>(async () =>
            await _handler.Handle(new LoginCommand("missing@example.com", "password123"), CancellationToken.None));

        // No-leak: the exception message is the closed-table title — never "user not found" or the raw email.
        Assert.Contains("Wrong password", ex.Message);

        // login_failed emitted on the wrong-password path (task §4).
        Assert.Single(_telemetryLogger.Entries, e => e.RenderedText == TelemetryEvents.LoginFailed);
        Assert.Empty(_users.Created);
    }

    // ---- FR-008-5: password < 8 chars → LoginValidationException before any DB read or telemetry ----

    [Fact]
    public async Task PasswordUnderEightChars_ThrowsValidationBeforeLookup()
    {
        var userId = Guid.NewGuid();
        _users.ExistingUser = SeedRealAppUser(userId);

        await Assert.ThrowsAsync<LoginValidationException>(async () =>
            await _handler.Handle(new LoginCommand("real@example.com", "1234567"), CancellationToken.None));

        // No row read (lookup never reached), no telemetry on the validation path.
        Assert.Empty(_users.Created);
        Assert.Empty(_telemetryLogger.Entries);
    }

    [Fact]
    public async Task Login_BadEmailFormat_ThrowsValidationBeforeLookup()
    {
        await Assert.ThrowsAsync<LoginValidationException>(async () =>
            await _handler.Handle(new LoginCommand("not-an-email", "password123"), CancellationToken.None));

        Assert.Empty(_users.Created);
        Assert.Empty(_telemetryLogger.Entries); // no telemetry on the validation path
    }

    // ---- Stubs (T-008-01 style — hand-written stubs, no Moq) ----

    private sealed class StubAppUserStore : IAppUserStore
    {
        public List<AppUser> Created { get; } = [];
        public AppUser? ExistingUser { get; set; }

        // The store models a real DB — only returns a row when the stored (lowercased) email matches exactly.
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
            Task.FromResult(ExistingUser?.Email == lowercasedEmail ? ExistingUser : null);

        public Task CreateAsync(AppUser user, CancellationToken ct)
        {
            Created.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPlansStore : IPlansStore
    {
        // FR-004: "Free by default" is a seeded row — test stub mirrors the seed (TA-3.4).
        public Plan Plan { get; } = new() { Id = Guid.NewGuid(), Code = "free", Name = "Free" };

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            planCode == "free" ? Task.FromResult(Plan) : throw new PlanUnknownException(planCode);

        // T-008-02: by-Id read — returns the seeded row or null (never throws).
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plan.Id == planId ? Plan : null);
    }

    /// <summary>T-008-01 style: records exactly what M.E.L hands to the logger (same as WaTelemetryReporterTests).</summary>
    internal sealed class TelemetryRecorder : ILogger<WaTelemetryReporter>
    {
        public record Entry(string RenderedText, object?[] Args);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

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
