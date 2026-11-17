using System.Net.Mail;
using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/forgot-password request (TA-4.2#11). Body: { email }.</summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResult>;

/// <summary>
/// T-008-05 (FR-008-6): the only business logic behind POST /api/v1/auth/forgot-password.
/// Anti-enumeration (D-1): for an unregistered email NO AuthToken row, NO event, NO send — yet the same
/// ForgotPasswordResult is returned so the endpoint emits a byte-identical 202 Accepted (no body) either way.
/// For a registered email: generates a 256-bit random token; stores its SHA-256 hex as AuthToken.TokenHash
/// (Purpose=2, 10-min TTL reused from RequestMagicLinkHandler.TtlSeconds); publishes auth.magic_link_requested
/// with an extra purpose=2 payload field — the D-3 discriminator EmailFunction routes on (D-2: event reuse).
/// Redeem (endpoint 10) filters on TokenHash only — no Purpose check — so Purpose=2 signs in exactly like Purpose=1.
/// </summary>
public sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    /// <summary>Purpose code for forgot-password tokens (TA-3.2: AuthToken.Purpose; 2=forgot-password).</summary>
    private const byte PurposeForgotPassword = 2;

    private readonly IAppUserStore _users;
    private readonly IAuthTokenStore _tokens;
    private readonly IEventPublisher _eventPublisher;

    public ForgotPasswordHandler(
        IAppUserStore users,
        IAuthTokenStore tokens,
        IEventPublisher eventPublisher)
    {
        _users = users;
        _tokens = tokens;
        _eventPublisher = eventPublisher;
    }

    public async Task<ForgotPasswordResult> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        // 1. Normalize + validate email (same form as RequestMagicLinkHandler/SignupHandler).
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw ForgotPasswordValidationException.ForInvalidEmail();

        // 2. Anti-enumeration gate BEFORE any row/event: unknown email → no row, no event, no send (FR-008-6).
        var user = await _users.FindByEmailAsync(email, cancellationToken);
        if (user is null)
            return new ForgotPasswordResult();

        // 3. Generate a 256-bit random token (32 bytes → base64).
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawTokenBytes); // the raw value that goes into the email link (?ml={token})
        var tokenHashHex = ComputeSha256Hex(rawToken); // only the hash is persisted (TA-9.1)

        // 4. Persist the AuthToken row with Purpose=2 and the frozen 10-minute TTL.
        var nowUtc = DateTime.UtcNow;
        var authToken = new AuthToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            TokenHash = tokenHashHex,
            Purpose = PurposeForgotPassword,
            ExpiresAtUtc = nowUtc.AddSeconds(RequestMagicLinkHandler.TtlSeconds), // frozen 600 s — same assembly constant
            RedeemedAtUtc = null, // unused until redeem (single-use)
            CreatedAtUtc = nowUtc
        };
        await _tokens.CreateAsync(authToken, cancellationToken);

        // 5. Build the TA-5.3 auth.magic_link_requested envelope — RAW token in the payload (not the hash).
        // The purpose field (4th) is the D-3 dispatch discriminator read only by EmailFunction; handlers ignore it.
        var payload = new
        {
            email = email,
            token = rawToken, // base64 — f-email worker renders it into the CTA URL (?ml={token})
            expiresAtUtc = authToken.ExpiresAtUtc,
            purpose = PurposeForgotPassword
        };

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.MagicLinkRequested, // D-2: reuse — the 13th EventType member was rejected
            Version = 1,
            OccurredAtUtc = nowUtc,
            CorrelationId = GetCorrelationGuid(),
            PartitionKey = email,
            Payload = payload
        };

        // 6. Emit via outbox (TA-4.1.7 at-most-once accepted at M0).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        return new ForgotPasswordResult();
    }

    /// <summary>Compute SHA-256 hex (64 chars) of a raw token string.</summary>
    private static string ComputeSha256Hex(string token)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Derives the correlation Guid from the W3C trace-id (same convention as SendTransferHandler).</summary>
    private static Guid GetCorrelationGuid()
    {
        var traceId = CorrelationContext.CurrentRequest?.TraceId;
        if (traceId is not null && traceId.Length == 32)
        {
            var hex = traceId.Replace("-", "");
            if (hex.Length == 32 && System.Linq.Enumerable.All(hex, c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return new Guid(hex);
        }
        return Guid.NewGuid();
    }

    /// <summary>TA-4.2#8: shared normalization — lowercase + trim (same form as SignupHandler).</summary>
    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Validates an email address using .NET's MailAddress (TA-4.2a style: no regexes). Empty/blank is rejected (missing email).</summary>
    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
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

/// <summary>T-008-05: request result — 202 Accepted with no body fields (byte-identical for registered/unregistered).</summary>
public sealed record ForgotPasswordResult();

/// <summary>
/// T-008-05: validation failure inside the forgot-password use case. The endpoint catches and maps to
/// VALIDATION via the closed code list (TA-4.1.3) — same pattern as RequestMagicLinkValidationException (T-008-04).
/// </summary>
public sealed class ForgotPasswordValidationException : Exception
{
    private ForgotPasswordValidationException(string message) : base(message) { }

    public static ForgotPasswordValidationException ForInvalidEmail() =>
        new("A valid email address is required."); // same text as signup/magic-link (consistency)
}
