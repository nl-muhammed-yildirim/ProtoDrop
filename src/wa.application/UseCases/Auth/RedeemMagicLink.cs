using System.Net.Mail;
using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/magic-link/redeem request (TA-4.2#9). Body: { token }.</summary>
public sealed record RedeemMagicLinkCommand(string Token) : IRequest<RedeemMagicLinkResult>;

/// <summary>
/// T-008-04 (FR-008-1 / FR-008-3): redeem a magic-link token — verify single-use + expiry, sign in, mint session token.
/// Unknown email → create AppUser on redeem (sign-up is sign-in); user_created emitted BEFORE login_success.
/// Expired/redeemed/unknown → LinkExpiredException (410 Gone at endpoint).
/// </summary>
public sealed class RedeemMagicLinkHandler : IRequestHandler<RedeemMagicLinkCommand, RedeemMagicLinkResult>
{
    /// <summary>Frozen Magic link TTL — 10 minutes (Open-Decisions-and-Constants.md Part 2, TA-9.1).</summary>
    public const int TtlSeconds = 600;

    private readonly IAuthTokenStore _tokens;
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly WaTelemetryReporter _telemetry;
    private readonly ISessionTokenMinter _minter;

    public RedeemMagicLinkHandler(
        IAuthTokenStore tokens,
        IAppUserStore users,
        IPlansStore plans,
        WaTelemetryReporter telemetry,
        ISessionTokenMinter minter)
    {
        _tokens = tokens;
        _users = users;
        _plans = plans;
        _telemetry = telemetry;
        _minter = minter;
    }

    public async Task<RedeemMagicLinkResult> Handle(RedeemMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Validate token is non-empty and base64-decodable (TA-4.2a: malformed ≠ unknown hash — VALIDATION at endpoint).
        if (string.IsNullOrWhiteSpace(request.Token))
            throw RedeemValidationException.ForMissingToken();

        try { _ = Convert.FromBase64String(request.Token); } // our tokens are always base64 — reject malformed early (400, not 410)
        catch (FormatException) { throw RedeemValidationException.ForInvalidToken(); }

        // 2. Compute SHA-256 hex of the raw token and look up the AuthToken row.
        var tokenHashHex = ComputeSha256Hex(request.Token);
        var authToken = await _tokens.FindByHashAsync(tokenHashHex, cancellationToken);

        // 3. EC-008-3: null (unknown hash), expired, OR already redeemed → same LinkExpiredException (don't leak which).
        if (authToken is null || authToken.RedeemedAtUtc is not null || DateTime.UtcNow > authToken.ExpiresAtUtc)
            throw LinkExpiredException.ForLinkExpired();

        // 4. Mark the token redeemed (single-use, first redeem wins — AC-008-2).
        await _tokens.MarkRedeemedAsync(authToken.Id, DateTime.UtcNow, cancellationToken);

        // 5. Look up the user by email (already lowercased + stored in AuthToken row).
        var email = authToken.Email;
        var existingUser = await _users.FindByEmailAsync(email, cancellationToken);

        AppUser? user;
        if (existingUser is null)
        {
            // FR-008-3: sign-up is sign-in — create the AppUser on redeem.
            var freePlan = await _plans.GetAsync("free", cancellationToken);
            user = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                EmailConfirmed = true, // magic link IS the confirmation (T-008-04)
                DisplayName = DeriveDisplayName(email),
                PasswordHash = null,
                PlanId = freePlan.Id,
                Theme = "system",
                CreatedAtUtc = DateTime.UtcNow
            };
            await _users.CreateAsync(user, cancellationToken);
            _telemetry.LogEvent(TelemetryEvents.UserCreated); // user_created emitted BEFORE login_success (plan D-2)
        }
        else
        {
            user = existingUser;
        }

        // 6. Resolve plan code from user's PlanId.
        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw LinkExpiredException.ForLinkExpired(); // plan row missing → same shape as expired (no-leak)

        // 7. Mint session token + emit login_success telemetry.
        _telemetry.LogEvent(TelemetryEvents.LoginSuccess); // login_success emitted exactly once on success (D-4)

        return new RedeemMagicLinkResult(
            user.Id,
            user.Email,
            user.DisplayName,
            user.Theme,
            plan.Code,
            _minter.Mint(user.Id));
    }

    /// <summary>Compute SHA-256 hex (64 chars) of the raw token string — same algorithm as RequestMagicLinkHandler (TA-9.1).</summary>
    private static string ComputeSha256Hex(string token)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>FR-008-4: DisplayName defaults from the email local part — nothing else to derive it from.</summary>
    private static string DeriveDisplayName(string lowercasedEmail) => lowercasedEmail.Split('@')[0];
}

/// <summary>
/// T-008-04 (D-4): the single use-case exception for expired/redeemed/unknown magic-link tokens.
/// Maps to 410 Gone + ProblemCodes.LinkExpired at the endpoint (EC-008-3: same shape for all three).
/// </summary>
public sealed class LinkExpiredException : Exception
{
    private LinkExpiredException(string message) : base(message) { }

    public static LinkExpiredException ForLinkExpired() =>
        new(ProblemCodes.TitleOf(ProblemCodes.LinkExpired)); // title from the closed table — never leaks which emails exist
}

/// <summary>T-008-04: validation failure inside the redeem use case. Maps to VALIDATION (400) at the endpoint.</summary>
public sealed class RedeemValidationException : Exception
{
    private RedeemValidationException(string message) : base(message) { }

    public static RedeemValidationException ForMissingToken() =>
        new("A magic link token is required.");

    public static RedeemValidationException ForInvalidToken() =>
        new("The provided magic link token is malformed.");
}

/// <summary>
/// T-008-04 (D-4): redeem result — same profile DTO shape as LoginResult + session token value.
/// The wire shape is pinned at the endpoint's single construction site (TA-4.2a thin-endpoint rule).
/// </summary>
public sealed record RedeemMagicLinkResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
