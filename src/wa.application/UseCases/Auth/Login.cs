using System.Net.Mail;
using MediatR;
using Microsoft.AspNetCore.Identity;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/login request (TA-4.2#9). Body: { email, password }.</summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

/// <summary>
/// POST /api/v1/auth/login result — the profile DTO plus the TA-9.1 session-token value.
/// Same shape as SignupResult; the wire shape is pinned at the endpoint's single construction site (TA-4.2a).
/// </summary>
public sealed record LoginResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);

/// <summary>
/// T-008-02 (TA-4.2#9 / FR-008-1): the only business logic behind POST /api/v1/auth/login.
/// Normalize email (same form as SignupHandler.NormalizeEmail — EC-008-1); no row OR failed hash verify → one single
/// use-case exception mapped to WRONG_PASSWORD at the endpoint — never "user not found" (EC: don't leak which emails exist).
/// </summary>
public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ISessionTokenMinter _tokens;

    public LoginHandler(
        IAppUserStore users,
        IPlansStore plans,
        WaTelemetryReporter telemetry,
        ISessionTokenMinter tokens)
    {
        _users = users;
        _plans = plans;
        _telemetry = telemetry;
        _tokens = tokens;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw LoginValidationException.ForInvalidEmail(); // bad format → VALIDATION at the endpoint (mirrors T-008-01; never row-vs-hash)
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw LoginValidationException.ForShortPassword(); // FR-008-5: server-side re-check — same shape as signup

        AppUser? user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            _telemetry.LogEvent(TelemetryEvents.LoginFailed); // AC-008-1 / task §4: login_failed on the wrong-password path
            throw LoginWrongPasswordException.ForWrongPassword(); // no row → same shape as wrong password (no-leak)
        }

        // TA-9.1: PBKDF2-SHA256 via ASP.NET Identity PasswordHasher<string>, keyed by userId.ToString("n") — same keying as signup.
        var hasher = new PasswordHasher<string>();
        if (hasher.VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, request.Password) is not
            (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
        {
            _telemetry.LogEvent(TelemetryEvents.LoginFailed); // AC-008-1 / task §4: login_failed on the wrong-password path
            throw LoginWrongPasswordException.ForWrongPassword(); // failed verify → same shape as no row (no-leak)
        }

        // FR-008-1: plan code resolved from AppUser.PlanId — must match signup's value for the same user.
        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw LoginWrongPasswordException.ForWrongPassword(); // plan row missing → same shape (no-leak; never a new code)

        _telemetry.LogEvent(TelemetryEvents.LoginSuccess); // AC-008-1: login_success emitted exactly once on success

        return new LoginResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Theme,
            plan.Code,
            _tokens.Mint(user.Id)); // TA-9.1: 30-day rolling session token — the endpoint sets the same value as the wa.session cookie
    }

    /// <summary>TA-9.1: shared normalization — lowercase + trim (same form EC-008-1 requires; same as T-008-01's SignupHandler).</summary>
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Validates an email address using .NET's MailAddress (TA-4.2a style: no regexes; mirrors T-008-01).</summary>
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
/// T-008-02: validation failure inside the login use case — same pattern as SignupValidationException (T-008-01).
/// The endpoint catches and maps to VALIDATION (400) via the closed code list (TA-4.1.3); no new code added.
/// </summary>
public sealed class LoginValidationException : Exception
{
    private LoginValidationException(string message) : base(message) { }

    public static LoginValidationException ForShortPassword() =>
        new("Password must be at least 8 characters."); // FR-008-5: same text as signup (consistency)

    public static LoginValidationException ForInvalidEmail() =>
        new("A valid email address is required."); // same text as signup (consistency)
}

/// <summary>
/// T-008-02: the single use-case exception for row-vs-hash failures — every auth failure funnels through it so the wire shape
/// never leaks which emails exist (no "user not found"; don't leak which emails exist). The endpoint maps it to
/// BusinessException(ProblemCodes.WrongPassword, ...) → 401 WRONG_PASSWORD (TA-4.1.3 closed list — already in the table; no new code).
/// </summary>
public sealed class LoginWrongPasswordException : Exception
{
    private LoginWrongPasswordException(string message) : base(message) { }

    public static LoginWrongPasswordException ForWrongPassword() =>
        new(ProblemCodes.TitleOf(ProblemCodes.WrongPassword)); // title from the closed table — never "user not found"
}
