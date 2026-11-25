using System.Text.RegularExpressions;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// A validation failure in the send use case (e.g. MAX_EMAILS exceeded). The endpoint catches this
/// and throws BusinessException(ProblemCodes.Validation, ex.Message) — keeps the closed code list intact.
/// </summary>
public sealed class TransferValidationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TransferValidationException"/> class.</summary>
    /// <param name="message">Human-readable reason that is returned to the client as the validation detail.</param>
    public TransferValidationException(string message) : base(message) { }
}
