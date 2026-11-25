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
/// <param name="State">Which outcome the download-all request had.</param>
/// <param name="Url">Ok only: the 30-minute SAS URL of the zip blob.</param>
/// <param name="PollAfterSec">Generating only: how long to wait before polling again.</param>
/// <param name="SenderEmail">Expired and DownloadLimit only: sender address shown in the problem detail; PII (TA-9.4).</param>
public sealed record DownloadAllResult(
    DownloadAllState State,
    Uri? Url = null,
    int? PollAfterSec = null,
    string? SenderEmail = null); // Expired/DownloadLimit only — renders into Problem+JSON details (FR-003-8/9)
