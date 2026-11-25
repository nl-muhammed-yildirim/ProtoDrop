using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Correlation;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>Result of GetDownloadAllUrlQuery — the endpoint maps this to HTTP; the handler stays pure CQRS (TA-4.2a).</summary>
public sealed record DownloadAllResult(
    DownloadAllState State,
    Uri? Url = null,          // Ok only — the 30-minute SAS on the zip blob
    int? PollAfterSec = null, // Generating only — how long to wait before re-polling
    string? SenderEmail = null); // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)
