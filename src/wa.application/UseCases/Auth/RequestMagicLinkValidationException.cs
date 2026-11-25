namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04: validation failure inside the magic-link request use case. The endpoint catches and maps to
/// VALIDATION via the closed code list (TA-4.1.3) — same pattern as SignupValidationException (T-008-01).
/// </summary>
public sealed class RequestMagicLinkValidationException : Exception
{
    private RequestMagicLinkValidationException(string message) : base(message) { }

    /// <summary>The submitted email is missing or not a valid address; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception with the standard "valid email required" message.</returns>
    public static RequestMagicLinkValidationException ForInvalidEmail() =>
        new("A valid email address is required."); // same text as signup (consistency)
}
