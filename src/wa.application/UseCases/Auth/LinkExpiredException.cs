using System.Security.Cryptography;
using MediatR;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (D-4): the single use-case exception for expired/redeemed/unknown magic-link tokens.
/// Maps to 410 Gone + ProblemCodes.LinkExpired at the endpoint (EC-008-3: same shape for all three).
/// </summary>
public sealed class LinkExpiredException : Exception
{
    private LinkExpiredException(string message) : base(message) { }

    /// <summary>Creates the link expired exception for link expired.</summary>
    /// <returns>The link expired exception.</returns>
    public static LinkExpiredException ForLinkExpired() =>
        new(ProblemCodes.TitleOf(ProblemCodes.LinkExpired)); // title from the closed table — never leaks which emails exist
}
