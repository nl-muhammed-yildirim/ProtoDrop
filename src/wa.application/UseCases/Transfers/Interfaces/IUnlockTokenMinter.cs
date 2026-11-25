using System.Text;
using System.Text.Json;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-003-04 (TA-4.2#5): the recipient unlock JWT — HS256 with claims { sub: linkId, pwd: true, exp: now+7d, jti },
/// passed as ?t= (no cookie). Minted on endpoint 5's correct password; verified by the endpoint 4/6/7 handlers.
/// Deliberately BCL-only (HMACSHA256 + Base64Url) — no System.IdentityModel package (TA-17: a JWT library is only justified
/// if T-003-06's SAS endpoints need one; the key arrives as a parameter, so the shared Wa:Jwt:Secret wiring lives in Program.cs).
/// </summary>
public interface IUnlockTokenMinter
{
    /// <summary>Mint a 7-day unlock token for the given linkId (TA-4.2#5 exact claim set).</summary>
    /// <param name="linkId">Identifier of the link.</param>
    /// <returns>The resulting text.</returns>
    string Mint(string linkId);

    /// <summary>
    /// Verify a ?t= token: well-formed 3 segments, correct HS256 signature, sub == expectedSub, pwd == true, exp in the future.
    /// Every failure (junk / tampered / wrong-sub / expired / missing or false pwd) returns false — never throws (TA-9.6 enumeration safety).
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="expectedSub">The expected sub.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    bool Verify(string? token, string expectedSub);
}
