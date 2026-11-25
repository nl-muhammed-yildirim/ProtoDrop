using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of GetFileDownloadUrlQuery — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a).</summary>
public sealed record FileDownloadResult(
    FileDownloadState State,
    Uri? Url = null,          // Ok only — the 30-minute SAS with the original-filename Content-Disposition override (EC-003-3)
    DateTimeOffset? ExpiresAt = null, // Ok only — the SAS expiry (wire shape { url, expiresAt }, 02-feature-plan §F-TRF-003)
    string? SenderEmail = null);      // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)
