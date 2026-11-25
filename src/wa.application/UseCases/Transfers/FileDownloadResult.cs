using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of GetFileDownloadUrlQuery — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a).</summary>
/// <param name="State">Which outcome the download request had.</param>
/// <param name="Url">
/// Ok only — the 30-minute SAS with the original-filename Content-Disposition override (EC-003-3).
/// </param>
/// <param name="ExpiresAt">
/// Ok only — the SAS expiry (wire shape { url, expiresAt }, 02-feature-plan §F-TRF-003).
/// </param>
/// <param name="SenderEmail">Expired and DownloadLimit only: sender address shown in the problem detail; PII (TA-9.4).</param>
public sealed record FileDownloadResult(
    FileDownloadState State,
    Uri? Url = null,
    DateTimeOffset? ExpiresAt = null,
    string? SenderEmail = null);      // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)
