namespace wa.application.UseCases.Auth;

/// <summary>
/// CHORE-staging-blockers r2 (TA-9.1): the one rule that decides whether a still-valid, still-signed session token has been revoked
/// by the account's session epoch (<c>AppUser.SessionsValidAfterUtc</c>). Session tokens are stateless, so this is the only server-side kill switch.
/// </summary>
public static class SessionRevocation
{
    /// <summary>
    /// A token is revoked when it was issued strictly before the epoch. Both sides are compared at millisecond precision (the token's issue-time resolution),
    /// so a session minted right after the epoch was set is never rejected by sub-millisecond truncation.
    /// </summary>
    /// <param name="issuedAtUtc">When the token was issued (UTC).</param>
    /// <param name="sessionsValidAfterUtc">The account's session epoch, or <see langword="null"/> when no session was ever revoked.</param>
    /// <returns><see langword="true"/> when the token must be treated as a guest session; <see langword="false"/> when it is accepted.</returns>
    public static bool IsRevoked(DateTime issuedAtUtc, DateTime? sessionsValidAfterUtc)
    {
        if (sessionsValidAfterUtc is not { } epoch)
            return false;

        return ToUnixMs(issuedAtUtc) < ToUnixMs(epoch);
    }

    private static long ToUnixMs(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
