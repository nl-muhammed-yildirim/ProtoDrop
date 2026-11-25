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
/// <param name="TransferId">Id of the draft transfer to send, from the route.</param>
/// <param name="SenderName">Sender name shown to recipients; null keeps the stored one; PII (TA-9.4).</param>
/// <param name="SenderEmail">Sender address for replies; null for none; PII (TA-9.4).</param>
/// <param name="Note">Optional message shown on the download page.</param>
/// <param name="Emails">Recipient addresses to notify; may be empty; PII (TA-9.4).</param>
/// <param name="Password">Optional plain-text password to protect the transfer; hashed before it is stored.</param>
/// <param name="OwnerAppUserId">T-008-08 (FR-008-7): session owner at send time — guest → null.</param>
/// <param name="ScheduledSendAtUtc">Requested send time in UTC; null sends immediately.</param>
public sealed record SendTransferCommand(
    Guid TransferId,
    string? SenderName,
    string? SenderEmail,
    string? Note,
    IReadOnlyList<string> Emails,
    string? Password = null,
    Guid? OwnerAppUserId = null,
    DateTime? ScheduledSendAtUtc = null) : IRequest<SendTransferResult?>;   // US-022-01 (FR-022-4): UTC due time; null = send now
