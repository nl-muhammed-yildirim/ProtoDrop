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

/// <summary>US-022-03 (FR-022-2): scheduledSendAt is outside now+5 min .. now+14 days (server time).
/// The endpoint maps it to 422 SCHEDULE_OUT_OF_WINDOW.</summary>
public sealed class ScheduleOutOfWindowException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ScheduleOutOfWindowException"/> class with the shared window message.</summary>
    public ScheduleOutOfWindowException() : base(ScheduleWindow.OutOfWindowMessage) { }
}
