using System.Net.Mail;
using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

/// <summary>POST /api/v1/auth/magic-link request (TA-4.2#10). Body: { email }.</summary>
public sealed record RequestMagicLinkCommand(string Email) : IRequest<RequestMagicLinkResult>;

/// <summary>
/// T-008-04 (FR-008-1 / TA-9.1): the only business logic behind POST /api/v1/auth/magic-link.
/// Generates a 256-bit random token; stores its SHA-256 hex as AuthToken.TokenHash (Purpose=1, 10-min TTL);
/// publishes auth.magic_link_requested via IEventPublisher so f-email worker sends the sign-in link.
/// Only the hash is persisted — the raw token goes in the event payload (TA-9.1: DB leak reveals no working link).
/// </summary>
public sealed class RequestMagicLinkHandler : IRequestHandler<RequestMagicLinkCommand, RequestMagicLinkResult>
{
    /// <summary>Frozen Magic link TTL — 10 minutes (Open-Decisions-and-Constants.md Part 2, TA-9.1).</summary>
    public const int TtlSeconds = 600;

    /// <summary>Purpose code for magic-link tokens (TA-3.2: AuthToken.Purpose; 1=magic-link-login).</summary>
    private const byte PurposeMagicLinkLogin = 1;

    private readonly IAuthTokenStore _tokens;
    private readonly IEventPublisher _eventPublisher;

    public RequestMagicLinkHandler(
        IAuthTokenStore tokens,
        IEventPublisher eventPublisher)
    {
        _tokens = tokens;
        _eventPublisher = eventPublisher;
    }

    public async Task<RequestMagicLinkResult> Handle(RequestMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Normalize + validate email (TA-4.2#8 style — same form as SignupHandler).
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw RequestMagicLinkValidationException.ForInvalidEmail();

        // 2. Generate a 256-bit random token (32 bytes → base64).
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawTokenBytes); // the raw value that goes into the email link (?ml={token})
        var tokenHashHex = ComputeSha256Hex(rawToken); // only the hash is persisted (TA-9.1)

        // 3. Persist the AuthToken row with Purpose=1 and 10-minute TTL.
        var nowUtc = DateTime.UtcNow;
        var authToken = new AuthToken
        {
            Id = Guid.NewGuid(),
            Email = email,
            TokenHash = tokenHashHex,
            Purpose = PurposeMagicLinkLogin,
            ExpiresAtUtc = nowUtc.AddSeconds(TtlSeconds),
            RedeemedAtUtc = null, // unused until redeem (single-use)
            CreatedAtUtc = nowUtc
        };
        await _tokens.CreateAsync(authToken, cancellationToken);

        // 4. Build the TA-5.3 auth.magic_link_requested envelope — RAW token in the payload (not the hash).
        var payload = new
        {
            email = email,
            token = rawToken, // base64 — f-email worker renders it into the CTA URL (?ml={token})
            expiresAtUtc = authToken.ExpiresAtUtc
        };

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.MagicLinkRequested,
            Version = 1,
            OccurredAtUtc = nowUtc,
            CorrelationId = GetCorrelationGuid(),
            PartitionKey = email,
            Payload = payload
        };

        // 5. Emit via outbox (TA-4.1.7 at-most-once accepted at M0).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        return new RequestMagicLinkResult();
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

/// <summary>T-008-04: request result — 202 Accepted with no body fields.</summary>
public sealed record RequestMagicLinkResult();

/// <summary>
/// T-008-04: validation failure inside the magic-link request use case. The endpoint catches and maps to
/// VALIDATION via the closed code list (TA-4.1.3) — same pattern as SignupValidationException (T-008-01).
/// </summary>
public sealed class RequestMagicLinkValidationException : Exception
{
    private RequestMagicLinkValidationException(string message) : base(message) { }

    public static RequestMagicLinkValidationException ForInvalidEmail() =>
        new("A valid email address is required."); // same text as signup (consistency)
}
