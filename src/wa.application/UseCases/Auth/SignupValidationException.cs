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
/// T-008-01: validation failure inside the signup use case. The endpoint catches and maps to VALIDATION
/// via the closed code list (TA-4.1.3) — same pattern as TransferValidationException (T-002-04).
/// </summary>
public sealed class SignupValidationException : Exception
{
    private SignupValidationException(string message) : base(message) { }

    /// <summary>The submitted password is shorter than 8 characters; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception carrying the "password too short" message.</returns>
    public static SignupValidationException ForShortPassword() =>
        new("Password must be at least 8 characters.");

    /// <summary>The submitted email is not a valid address; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception carrying the "valid email required" message.</returns>
    public static SignupValidationException ForInvalidEmail() =>
        new("A valid email address is required.");
}
