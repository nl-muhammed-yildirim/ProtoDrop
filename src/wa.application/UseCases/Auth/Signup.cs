using System.Net.Mail;
using System.Security.Cryptography;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/signup request (TA-4.2#8). Body: { email, password }.</summary>
public sealed record SignupCommand(string Email, string Password) : IRequest<SignupResult>;

/// <summary>
/// POST /api/v1/auth/signup result — the profile DTO plus the TA-9.1 session-token value.
/// The wire shape is pinned at the endpoint's single construction site (TA-4.2a thin-endpoint rule).
/// </summary>
public sealed record SignupResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);

/// <summary>
/// T-008-01 (TA-4.2#8 / FR-008-3): sign-up is sign-in — the only business logic behind POST /api/v1/auth/signup.
/// Unknown email → create AppUser (lowercased + trimmed email, DisplayName from local part, plan Free); known email (any case) → sign into that same row.
/// user_created emitted once on create; login_success emitted on both branches (US-008-01 edge-case note).
/// </summary>
public sealed class SignupHandler : IRequestHandler<SignupCommand, SignupResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ISessionTokenMinter _tokens;
    private readonly ILogger<SignupHandler> _logger;

    public SignupHandler(
        IAppUserStore users,
        IPlansStore plans,
        WaTelemetryReporter telemetry,
        ISessionTokenMinter tokens,
        ILogger<SignupHandler> logger)
    {
        _users = users;
        _plans = plans;
        _telemetry = telemetry;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<SignupResult> Handle(SignupCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw SignupValidationException.ForInvalidEmail();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw SignupValidationException.ForShortPassword();

        // Resolve Free plan once — FR-004: "Free by default" is a seeded row, never a literal (TA-3.4).
        var freePlan = await _plans.GetAsync("free", cancellationToken);

        // EC-008-1/4: lookup by lowercased email — sign-up is sign-in; known emails return the existing row.
        var existing = await _users.FindByEmailAsync(email, cancellationToken);

        AppUser user;
        if (existing is null)
        {
            var hasher = new PasswordHasher<string>(); // PBKDF2-SHA256 — same ADR'd package as SendTransfer (TA-9.1).
            var userId = Guid.NewGuid();
            user = new AppUser
            {
                Id = userId,
                Email = email, // already lowercased + trimmed (EC-008-1)
                EmailConfirmed = false, // DB default — T-008-04 magic link flips it later
                DisplayName = DeriveDisplayName(email), // FR-008-4: local part of the email
                PasswordHash = hasher.HashPassword(userId.ToString("n"), request.Password),
                PlanId = freePlan.Id,
                Theme = "system" // DB default — no input on signup
            };
            await _users.CreateAsync(user, cancellationToken);
            _telemetry.LogEvent(TelemetryEvents.UserCreated); // AC-008-1: user_created emitted exactly once
            _logger.LogInformation("User created for signup email {email}", email);
        }
        else
        {
            user = existing; // EC-008-4: known email (any case) → sign into that row, no duplicate
        }

        _telemetry.LogEvent(TelemetryEvents.LoginSuccess); // both branches (US-008-01 edge-case note)

        return new SignupResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Theme,
            freePlan.Code,
            _tokens.Mint(user.Id));
    }

    /// <summary>TA-9.1: shared normalization — lowercase + trim (same form EC-008-1 requires).</summary>
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>FR-008-4: DisplayName defaults from the email local part — nothing else to derive it from.</summary>
    private static string DeriveDisplayName(string lowercasedEmail) => lowercasedEmail.Split('@')[0];

    /// <summary>Validates an email address using .NET's MailAddress (TA-4.2a style: no regexes).</summary>
    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// T-008-01: validation failure inside the signup use case. The endpoint catches and maps to VALIDATION
/// via the closed code list (TA-4.1.3) — same pattern as TransferValidationException (T-002-04).
/// </summary>
public sealed class SignupValidationException : Exception
{
    private SignupValidationException(string message) : base(message) { }

    public static SignupValidationException ForShortPassword() =>
        new("Password must be at least 8 characters.");

    public static SignupValidationException ForInvalidEmail() =>
        new("A valid email address is required.");
}

/// <summary>
/// TA-9.1 session-token minter: Base64(userId|exp|sig) where sig = HMAC-SHA256 over the first two segments.
/// Frozen 30-day rolling TTL (Open-Decisions-and-Constants.md Part 2 FROZEN — not a plan limit, TA-3.4).
/// The shared Wa:Jwt:Secret bytes arrive as a parameter (T-008-02 lands the cookie middleware reading this same value).
/// </summary>
public interface ISessionTokenMinter
{
    /// <summary>Mint a 30-day session token for the given user id (TA-9.1).</summary>
    string Mint(Guid userId);
}

public sealed class SessionTokenService : ISessionTokenMinter
{
    /// <summary>Frozen Session TTL — 30 days rolling (Open-Decisions-and-Constants.md Part 2, TA-9.1). Not a plan limit (TA-3.4).</summary>
    public const int LifetimeDays = 30;

    private readonly byte[] _key;
    private readonly Func<DateTimeOffset> _clock;

    public SessionTokenService(byte[] key, Func<DateTimeOffset> clock)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string Mint(Guid userId)
    {
        var now = _clock();
        var expSeconds = (now + TimeSpan.FromDays(LifetimeDays)).ToUnixTimeSeconds();
        var unsigned = $"{userId:N}|{expSeconds}";
        using var hmac = new HMACSHA256(_key);
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(unsigned)));
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }
}
