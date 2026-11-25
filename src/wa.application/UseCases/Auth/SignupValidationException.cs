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

    /// <summary>Creates the signup validation exception for short password.</summary>
    /// <returns>The signup validation exception.</returns>
    public static SignupValidationException ForShortPassword() =>
        new("Password must be at least 8 characters.");

    /// <summary>Creates the signup validation exception for invalid email.</summary>
    /// <returns>The signup validation exception.</returns>
    public static SignupValidationException ForInvalidEmail() =>
        new("A valid email address is required.");
}
