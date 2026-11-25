using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Auth;

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
