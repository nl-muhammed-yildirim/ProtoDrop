using System.Security.Cryptography;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// TA-9.1 session-token minter + reader: Base64(userId|exp|sig) where sig = HMAC-SHA256 over the first two segments.
/// Frozen 30-day rolling TTL (Open-Decisions-and-Constants.md Part 2 FROZEN — not a plan limit, TA-3.4).
/// Minter + reader live in one class so the cookie format can't drift (T-008-02's SessionMiddleware is the read site).
/// </summary>
public interface ISessionTokenMinter
{
    /// <summary>Mint a 30-day session token for the given user id (TA-9.1).</summary>
    /// <param name="userId">Primary key of the user the session is for.</param>
    /// <returns>The signed, base64-encoded session token to put in the cookie.</returns>
    string Mint(Guid userId);

    /// <summary>
    /// TA-9.1 PARSE side: decode + verify the signed session token — returns the resolved user id + expiry, or null on
    /// missing/malformed/tampered/expired (TA-9.1: guest path). The SessionMiddleware is the single read site (T-008-02).
    /// </summary>
    /// <param name="token">Raw cookie value; may be null or empty.</param>
    /// <returns>The user id and expiry of a valid token, or <see langword="null"/> when it is missing, malformed, tampered with or expired.</returns>
    (Guid UserId, DateTime ExpUtc)? Parse(string? token);

    /// <summary>
    /// FR-008-2 / TA-9.1 30-day rolling: true when the token has less than RenewRemainingDays remaining — the middleware
    /// re-mints + re-signs at that threshold (the "refreshed on use" rule). Pure helper over frozen values — no I/O.
    /// </summary>
    /// <param name="expUtc">Expiry of the current token.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns><see langword="true"/> when the token is close enough to expiry that it should be re-issued; otherwise <see langword="false"/>.</returns>
    bool NeedsRenewal(DateTime expUtc, DateTime nowUtc);
}
