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

/// <summary>US-022-01 (FR-022-3): the sender's plan (or the global feature.scheduling gate) does not allow scheduling.
/// The endpoint maps it to 403 FORBIDDEN, keeping the closed code list intact.</summary>
public sealed class ScheduleForbiddenException : Exception
{
    public ScheduleForbiddenException() : base("Pro plan required.") { }
}
