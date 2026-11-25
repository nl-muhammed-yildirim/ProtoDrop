using MediatR;
using Microsoft.AspNetCore.Identity;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-02: validation failure inside the login use case — same pattern as SignupValidationException (T-008-01).
/// The endpoint catches and maps to VALIDATION (400) via the closed code list (TA-4.1.3); no new code added.
/// </summary>
public sealed class LoginValidationException : Exception
{
    private LoginValidationException(string message) : base(message) { }

    public static LoginValidationException ForShortPassword() =>
        new("Password must be at least 8 characters."); // FR-008-5: same text as signup (consistency)

    public static LoginValidationException ForInvalidEmail() =>
        new("A valid email address is required."); // same text as signup (consistency)
}
