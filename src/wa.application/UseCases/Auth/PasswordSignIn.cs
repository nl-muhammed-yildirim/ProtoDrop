using Microsoft.AspNetCore.Identity;
using wa.domain;

namespace wa.application.UseCases.Auth;

/// <summary>
/// TA-9.1: the single password sign-in check shared by login and by signup on a known email (CHORE-staging-blockers),
/// so the two auth paths cannot drift apart again.
/// </summary>
internal static class PasswordSignIn
{
    private static readonly string DummyHash = new PasswordHasher<string>().HashPassword("dummy", "dummy-password-for-timing");

    /// <summary>Runs a PBKDF2 verify against a fixed dummy hash so the unknown-email path costs the same as a real verification; the result is discarded.</summary>
    /// <param name="password">Plain-text password from the request.</param>
    internal static void BurnVerifyCost(string password) =>
        _ = new PasswordHasher<string>().VerifyHashedPassword("dummy", DummyHash, password);

    /// <summary>
    /// Decides whether the given password signs the account in: PBKDF2-SHA256 via <see cref="PasswordHasher{TUser}"/>, keyed by <c>userId.ToString("n")</c>.
    /// A soft-deleted account and a magic-link-only account (no hash) are never accepted.
    /// </summary>
    /// <param name="user">Account found by email.</param>
    /// <param name="password">Plain-text password from the request.</param>
    /// <returns><see langword="true"/> when the password verifies for a live account; <see langword="false"/> for a soft-deleted account, an account without a password hash or a wrong password.</returns>
    internal static bool Accepts(AppUser user, string password)
    {
        if (user.DeletedAtUtc is not null || user.PasswordHash is null)
            return false;

        var result = new PasswordHasher<string>().VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
