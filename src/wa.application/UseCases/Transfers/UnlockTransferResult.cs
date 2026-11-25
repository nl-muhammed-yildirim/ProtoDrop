using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of UnlockTransferCommand — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a). Token only on Active; SenderEmail only on terminal states (renders into Problem+JSON details, FR-003-8/9).</summary>
/// <param name="State">Outcome of the unlock attempt.</param>
/// <param name="Token">Active only: the 7-day HS256 unlock token to pass as the <c>t</c> query parameter.</param>
/// <param name="SenderEmail">Expired and DownloadLimit only: sender address shown in the problem detail; PII (TA-9.4).</param>
public sealed record UnlockTransferResult(
    UnlockState State,
    string? Token = null,
    string? SenderEmail = null);    // Expired/DownloadLimit only
