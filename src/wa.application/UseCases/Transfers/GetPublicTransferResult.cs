using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of GetPublicTransferQuery — the endpoint maps this to HTTP; the handler stays pure CQRS. Only the Active branch carries payload fields; terminal states never do (no byte leak).</summary>
public sealed record GetPublicTransferResult(
    PublicTransferState State,
    string? From = null,            // Active only
    string? Note = null,            // Active only — JSON null when the sender left it empty
    IReadOnlyList<PublicFileItem>? Files = null,   // Active only — never on terminal states (no byte leak)
    bool HasDownloadAll = false,    // Active only
    int? DownloadsLeft = null,      // Active only; null when the cap is ∞
    bool PasswordRequired = false,  // Active only
    string? SenderEmail = null,     // terminal bodies render it into Problem+JSON details (FR-003-8/9)
    string? OrgName = null,         // Active only — sender's org when their plan has Branding (F-PRF-001)
    string? OrgLogoUrl = null,      // Active only — 30-min inline read SAS when branding is on and the blob exists
    DateTime? ScheduledSendAtUtc = null, // Active only — set only for Status Scheduled (F-PRF-002)
    bool AdsEnabled = false);       // Active only — FR-024-3
