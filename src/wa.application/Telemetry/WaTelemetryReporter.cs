using Microsoft.Extensions.Logging;
using wa.domain.Telemetry;

namespace wa.application.Telemetry;

/// <summary>
/// The single telemetry exit (T-056-04 / AC-056-4): every event goes through here so App Insights only ever sees names from the closed TA-10.2 set — no ad-hoc variants, no typos.
/// Template contract (what T-056-05 asserts and what App Insights queries by at T-025 — "names are code"):
/// <list type="bullet">
/// <item>LogEvent(name) → the Serilog template text IS exactly name (e.g. "upload_started"), no properties beyond the log context (correlationId/traceparent land via T-056-01's FromLogContext enrichment — never re-add them).</item>
/// <item>UploadCompleted(...) → "upload_completed {bytes} bytes in {durationMs} ms after {retries} retries" — upload_completed is the only event TA-10.2 names properties for (bytes, durationMs, retries).</item>
/// </list>
/// PII slots (TA-9.4): raw file names and IPs never enter a property untransformed — pass them through <see cref="FileNameSlot"/>/<see cref="IpSlot"/> first.
/// No separate metrics SDK: metrics use the same closed list, emitted by this same pipeline when T-025 needs them (T-056-04 note).
/// </summary>
public sealed class WaTelemetryReporter
{
    private readonly ILogger<WaTelemetryReporter> _logger;
    private readonly byte[] _hmacKey; // TA-9.4: UTF-8 of Wa:Jwt:Secret, wired in at Program.cs (the reporter stays config-free — the domain transforms take key bytes as parameters)

    /// <summary>Initializes a new instance of the <see cref="WaTelemetryReporter"/> class.</summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    /// <param name="hmacKey">Secret key bytes for the IP and User-Agent hashes (UTF-8 of <c>Wa:Jwt:Secret</c>).</param>
    public WaTelemetryReporter(ILogger<WaTelemetryReporter> logger, byte[] hmacKey)
    {
        _logger = logger;
        _hmacKey = hmacKey;
    }

    /// <summary>Emit a closed-set event with no contract-named properties (the 20 property-free TA-10.2 events — e.g. upload_started, transfer_page_viewed).
    /// Throws <see cref="ArgumentException"/> for anything not in <see cref="TelemetryEvents.IsEvent"/>: ad-hoc names fail fast here, not silently later in App Insights (EC-056-4).</summary>
    /// <param name="eventName">One of the closed TA-10.2 event names from <see cref="TelemetryEvents"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="eventName"/> is not a closed-set event.</exception>
    public void LogEvent(string eventName)
    {
        if (!TelemetryEvents.IsEvent(eventName))
            throw new ArgumentException($"Not a closed TA-10.2 event: '{eventName}'. Add it to TelemetryEvents.cs first (EC-056-4).", nameof(eventName));

        _logger.LogInformation(eventName); // plain-string overload → template text is exactly the event name, nothing else in it
    }

    /// <summary>Emit upload_completed with its three contract-named properties — the only event TA-10.2 names properties for (line 816).</summary>
    /// <param name="bytes">Total bytes uploaded.</param>
    /// <param name="durationMs">Elapsed upload time in milliseconds.</param>
    /// <param name="retries">Number of chunk retries needed.</param>
    public void UploadCompleted(long bytes, long durationMs, int retries) =>
        _logger.LogInformation("upload_completed {bytes} bytes in {durationMs} ms after {retries} retries", bytes, durationMs, retries);

    /// <summary>Emit zip_generated with its two properties — sizeBytes and durationMs (T-004-01).</summary>
    /// <param name="sizeBytes">Size of the produced zip in bytes.</param>
    /// <param name="durationMs">Time taken to build the zip in milliseconds.</param>
    public void ZipGenerated(long sizeBytes, long durationMs) =>
        _logger.LogInformation("zip_generated {sizeBytes} bytes in {durationMs} ms", sizeBytes, durationMs);

    /// <summary>Emit expiry_job_lag_seconds metric (TA-10.2). Alert P1 at > 1800s (TA-10.3).</summary>
    /// <param name="seconds">How many seconds the expiry job ran behind its schedule.</param>
    public void ExpiryJobLag(long seconds) =>
        _logger.LogInformation("expiry_job_lag_seconds {seconds}", seconds);

    /// <summary>Emit email_failures_1h metric (TA-10.2). Alert P2 at > 50/h (TA-10.3).</summary>
    public void EmailFailures1h() =>
        _logger.LogInformation("email_failures_1h {count}", 1);

    /// <summary>TA-4.2#11 abuse signal: forgot-password request volume (FR-008-6). TA-9.4: no PII — the counter is just 1.</summary>
    public void ForgotPasswordRequested() =>
        _logger.LogInformation("forgot_password_requested {count}", 1);

    /// <summary>Emit storage_bytes_active metric (TA-10.2): the account's active-bytes read as FR-007-4 counts them, feeding the admin storage meter (F-TRF-011-2).</summary>
    /// <param name="bytes">Active storage in bytes.</param>
    public void StorageBytesActive(long bytes) =>
        _logger.LogInformation("storage_bytes_active {bytes}", bytes);

    /// <summary>Emit transfer_page_viewed with its one property — the linkId hash (TA-9.4 style: never the raw 8-char id in telemetry; T-003-01). Template text keeps the exact TA-10.2 name first — App Insights queries by it.</summary>
    /// <param name="linkIdHash">Hash of the transfer link id; never the raw id.</param>
    public void TransferPageViewed(string linkIdHash) =>
        _logger.LogInformation("transfer_page_viewed {linkId}", linkIdHash);

    // ---- TA-9.4 PII slots — "the reporter's property slots are where those transforms live" ----

    /// <summary>TA-9.4: file name → SHA-256 (deterministic). Call this before putting a file name into an event property.</summary>
    /// <param name="fileName">Raw file name.</param>
    /// <returns>The SHA-256 hex hash that is safe to place in a telemetry property.</returns>
    public string FileNameSlot(string fileName) => PiiTransforms.FileName(fileName);

    /// <summary>TA-9.4: IP → HMAC-SHA256 keyed by Wa:Jwt:Secret bytes. Call this before putting a client IP into an event property (DownloadEvent is the raw-IP exception — TA-10.5).</summary>
    /// <param name="ip">Raw client IP address.</param>
    /// <returns>The keyed HMAC hex hash that is safe to place in a telemetry property.</returns>
    public string IpSlot(string ip) => PiiTransforms.IpAddress(ip, _hmacKey);
}
