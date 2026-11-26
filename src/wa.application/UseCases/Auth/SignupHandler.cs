using System.Net.Mail;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-01 (TA-4.2#8 / FR-008-3): sign-up is sign-in — the only business logic behind POST /api/v1/auth/signup.
/// Unknown email → create AppUser (lowercased + trimmed email, DisplayName from local part, plan Free); known email (any case) → verified like login
/// (wrong or absent password, or a soft-deleted account → WRONG_PASSWORD), never a password-less sign-in.
/// user_created emitted once on create; login_success on success; login_failed on a rejected known email.
/// </summary>
/// <param name="users">Store used to find or create the account.</param>
/// <param name="plans">Store used to resolve the default plan and the plan code.</param>
/// <param name="telemetry">Reporter for the <c>user_created</c> and <c>login_success</c> events.</param>
/// <param name="tokens">Mints the session token returned to the caller.</param>
/// <param name="logger">Logger used to log the id of a newly created user.</param>
public sealed partial class SignupHandler(
    IAppUserStore users,
    IPlansStore plans,
    WaTelemetryReporter telemetry,
    ISessionTokenMinter tokens,
    ILogger<SignupHandler> logger) : IRequestHandler<SignupCommand, SignupResult>
{
    private readonly IAppUserStore _users = users;
    private readonly IPlansStore _plans = plans;
    private readonly WaTelemetryReporter _telemetry = telemetry;
    private readonly ISessionTokenMinter _tokens = tokens;
    private readonly ILogger<SignupHandler> _logger = logger;

    /// <inheritdoc />
    public async Task<SignupResult> Handle(SignupCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw SignupValidationException.ForInvalidEmail();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw SignupValidationException.ForShortPassword();

        // EC-008-1/4: lookup by lowercased email — a known email signs in only with the correct password (same check as login).
        var existing = await _users.FindByEmailAsync(email, cancellationToken);
        if (existing is not null)
            return await SignInExistingAsync(existing, request.Password, cancellationToken);

        // Resolve Free plan — FR-004: "Free by default" is a seeded row, never a literal (TA-3.4).
        var freePlan = await _plans.GetAsync(Plan.DefaultCode, cancellationToken);

        var hasher = new PasswordHasher<string>(); // PBKDF2-SHA256 — same ADR'd package as SendTransfer (TA-9.1).
        var userId = Guid.NewGuid();
        var user = new AppUser
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
        LogUserCreated(_logger, userId);

        _telemetry.LogEvent(TelemetryEvents.LoginSuccess);

        return new SignupResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Theme,
            freePlan.Code,
            _tokens.Mint(user.Id));
    }

    /// <summary>
    /// EC-008-4 + AC-008-1: signup on a known email behaves exactly like login — the password is verified, a failure emits <c>login_failed</c>
    /// and throws the same exception login throws (no token), and a success returns the user's own plan.
    /// </summary>
    /// <param name="user">The existing account found by email.</param>
    /// <param name="password">Plain-text password from the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed-in account with a freshly minted session token.</returns>
    /// <exception cref="LoginWrongPasswordException">The password is wrong, the account has no password or is soft-deleted, or its plan row is missing.</exception>
    private async Task<SignupResult> SignInExistingAsync(AppUser user, string password, CancellationToken cancellationToken)
    {
        if (!PasswordSignIn.Accepts(user, password))
        {
            _telemetry.LogEvent(TelemetryEvents.LoginFailed);
            throw LoginWrongPasswordException.ForWrongPassword();
        }

        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken)
            ?? throw LoginWrongPasswordException.ForWrongPassword(); // plan row missing → same shape as login (no-leak)

        _telemetry.LogEvent(TelemetryEvents.LoginSuccess);
        return new SignupResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, _tokens.Mint(user.Id));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "User created {UserId}")]
    private static partial void LogUserCreated(ILogger logger, Guid userId);
}
