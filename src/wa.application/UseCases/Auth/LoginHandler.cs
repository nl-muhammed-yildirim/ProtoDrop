using System.Net.Mail;
using MediatR;
using Microsoft.AspNetCore.Identity;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-02 (TA-4.2#9 / FR-008-1): the only business logic behind POST /api/v1/auth/login.
/// Normalize email (same form as SignupHandler.NormalizeEmail — EC-008-1); no row OR failed hash verify → one single
/// use-case exception mapped to WRONG_PASSWORD at the endpoint — never "user not found" (EC: don't leak which emails exist).
/// </summary>
/// <param name="users">Store used to look up the account by email.</param>
/// <param name="plans">Store used to resolve the plan code of the account.</param>
/// <param name="telemetry">Reporter for the <c>login_success</c> and <c>login_failed</c> events.</param>
/// <param name="tokens">Mints the session token returned to the caller.</param>
public sealed class LoginHandler(
    IAppUserStore users,
    IPlansStore plans,
    WaTelemetryReporter telemetry,
    ISessionTokenMinter tokens) : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppUserStore _users = users;
    private readonly IPlansStore _plans = plans;
    private readonly WaTelemetryReporter _telemetry = telemetry;
    private readonly ISessionTokenMinter _tokens = tokens;

    /// <inheritdoc />
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
        // A magic-link-only account has no PasswordHash: nothing to verify, so it is a failed verify (same shape as no row — no 500, no leak).
        if (user.PasswordHash is null
            || hasher.VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, request.Password) is not
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
