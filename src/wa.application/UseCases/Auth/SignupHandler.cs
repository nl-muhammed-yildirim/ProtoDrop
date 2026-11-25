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

    /// <summary>Initializes a new instance of the <see cref="SignupHandler"/> class.</summary>
    /// <param name="users">Store used to find or create the account.</param>
    /// <param name="plans">Store used to resolve the default plan and the plan code.</param>
    /// <param name="telemetry">Reporter for the <c>user_created</c> and <c>login_success</c> events.</param>
    /// <param name="tokens">Mints the session token returned to the caller.</param>
    /// <param name="logger">Logger for diagnostics.</param>
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

    /// <inheritdoc />
    public async Task<SignupResult> Handle(SignupCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw SignupValidationException.ForInvalidEmail();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw SignupValidationException.ForShortPassword();

        // Resolve Free plan once — FR-004: "Free by default" is a seeded row, never a literal (TA-3.4).
        var freePlan = await _plans.GetAsync(Plan.DefaultCode, cancellationToken);

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
            _logger.LogInformation("User created {UserId}", userId);
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
