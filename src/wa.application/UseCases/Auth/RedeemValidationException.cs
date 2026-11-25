using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-04: validation failure inside the redeem use case. Maps to VALIDATION (400) at the endpoint.</summary>
public sealed class RedeemValidationException : Exception
{
    private RedeemValidationException(string message) : base(message) { }

    public static RedeemValidationException ForMissingToken() =>
        new("A magic link token is required.");

    public static RedeemValidationException ForInvalidToken() =>
        new("The provided magic link token is malformed.");
}
