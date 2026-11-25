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
/// <param name="TransferId">Identifier of the transfer of the send transfer command.</param>
/// <param name="SenderName">The sender name of the send transfer command.</param>
/// <param name="SenderEmail">The sender email of the send transfer command.</param>
/// <param name="Note">The note of the send transfer command.</param>
/// <param name="Emails">The emails of the send transfer command.</param>
/// <param name="Password">The password of the send transfer command.</param>
/// <param name="OwnerAppUserId">T-008-08 (FR-008-7): session owner at send time — guest → null.</param>
/// <param name="ScheduledSendAtUtc">The scheduled send at UTC of the send transfer command.</param>
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null,
    Guid? OwnerAppUserId = null,
    DateTime? ScheduledSendAtUtc = null) : IRequest<SendTransferResult?>;   // US-022-01 (FR-022-4): UTC due time; null = send now
