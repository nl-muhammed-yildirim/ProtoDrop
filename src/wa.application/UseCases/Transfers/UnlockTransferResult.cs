using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of UnlockTransferCommand — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a). Token only on Active; SenderEmail only on terminal states (renders into Problem+JSON details, FR-003-8/9).</summary>
/// <param name="State">The state of the unlock transfer result.</param>
/// <param name="Token">Active only — the 7-day HS256 JWT (TA-4.2#5).</param>
/// <param name="SenderEmail">The sender email of the unlock transfer result.</param>
public sealed record UnlockTransferResult(
    UnlockState State,
    string? Token = null,
    string? SenderEmail = null);    // Expired/DownloadLimit only
