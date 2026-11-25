using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of GetPublicTransferQuery — the endpoint maps this to HTTP; the handler stays pure CQRS. Only the Active branch carries payload fields; terminal states never do (no byte leak).</summary>
/// <param name="State">The state of the get public transfer result.</param>
/// <param name="From">Active only.</param>
/// <param name="Note">Active only — JSON null when the sender left it empty.</param>
/// <param name="Files">Active only — never on terminal states (no byte leak).</param>
/// <param name="HasDownloadAll">Active only.</param>
/// <param name="DownloadsLeft">Active only; null when the cap is ∞.</param>
/// <param name="PasswordRequired">Active only.</param>
/// <param name="SenderEmail">Terminal bodies render it into Problem+JSON details (FR-003-8/9).</param>
/// <param name="OrgName">Active only — sender's org when their plan has Branding (F-PRF-001).</param>
/// <param name="OrgLogoUrl">Active only — 30-min inline read SAS when branding is on and the blob exists.</param>
/// <param name="ScheduledSendAtUtc">Active only — set only for Status Scheduled (F-PRF-002).</param>
/// <param name="AdsEnabled">Whether ads enabled of the get public transfer result.</param>
public sealed record GetPublicTransferResult(
    PublicTransferState State,
    string? From = null,
    string? Note = null,
    IReadOnlyList<PublicFileItem>? Files = null,
    bool HasDownloadAll = false,
    int? DownloadsLeft = null,
    bool PasswordRequired = false,
    string? SenderEmail = null,
    string? OrgName = null,
    string? OrgLogoUrl = null,
    DateTime? ScheduledSendAtUtc = null,
    bool AdsEnabled = false);       // Active only — FR-024-3
