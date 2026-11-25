namespace wa.application.UseCases.Auth;

/// <summary>T-008-04: validation failure inside the redeem use case. Maps to VALIDATION (400) at the endpoint.</summary>
public sealed class RedeemValidationException : Exception
{
    private RedeemValidationException(string message) : base(message) { }

    /// <summary>No token was submitted; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception with the "token required" message.</returns>
    public static RedeemValidationException ForMissingToken() =>
        new("A magic link token is required.");

    /// <summary>The token is not valid base64, so it cannot be one of ours; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception with the "token malformed" message.</returns>
    public static RedeemValidationException ForInvalidToken() =>
        new("The provided magic link token is malformed.");
}
