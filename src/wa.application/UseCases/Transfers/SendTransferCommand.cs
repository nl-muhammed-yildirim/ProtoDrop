using System.Text.RegularExpressions;
using MediatR;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Transfers;

namespace wa.application.UseCases.Transfers;

/// <summary>POST /api/v1/transfers/{id}/send request (TA-4.2#3).</summary>
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null,
    Guid? OwnerAppUserId = null,   // T-008-08 (FR-008-7): session owner at send time — guest → null
    DateTime? ScheduledSendAtUtc = null) : IRequest<SendTransferResult?>;   // US-022-01 (FR-022-4): UTC due time; null = send now
