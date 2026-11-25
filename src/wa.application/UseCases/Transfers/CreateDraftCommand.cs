using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/draft request — the full staged-file selection (TA-4.2#1).</summary>
public sealed record CreateDraftCommand(IReadOnlyList<CreateDraftFile> Files) : IRequest<CreateDraftResult>;
