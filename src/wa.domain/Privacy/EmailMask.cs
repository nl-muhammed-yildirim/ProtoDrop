namespace wa.domain.Privacy;

/// <summary>
/// US-047-03 (TA-9.4): masks an email for admins who may not see it in full — "dana@x.com" becomes "d****@x.com".
/// Pure and shared so other admin screens can reuse it.
/// </summary>
public static class EmailMask
{
    /// <summary>Replaces everything between the first character and the at-sign with asterisks; an address without a local part becomes four asterisks.</summary>
    /// <param name="email">Full email address; may be null or empty.</param>
    /// <returns>The masked address, or null when <paramref name="email"/> is null or empty.</returns>
    public static string? Mask(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return null;

        var at = email.IndexOf('@');
        if (at <= 0)
            return "****";

        return string.Concat(email.AsSpan(0, 1), "****", email.AsSpan(at));
    }
}
