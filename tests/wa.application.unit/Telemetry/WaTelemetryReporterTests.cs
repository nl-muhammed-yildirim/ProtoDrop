using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.unit.Telemetry;

// AC-056-4 (T-056-04): an upload_started-style event through WaTelemetryReporter carries the exact TA-10.2 template name and properties — no ad-hoc variants, no renames.
public class WaTelemetryReporterTests
{
    // Same key string as wa.domain.unit's PiiTransforms tests so both projects share one known HMAC vector (TA-9.4).
    private static readonly byte[] Key = System.Text.Encoding.UTF8.GetBytes("Wa:Jwt:Secret test key");

    // ---- Exact template shape (T-056-04 exit check 1 / TA-10.2 line 816) ----

    [Fact]
    public void LogEvent_EmitsExactlyTheClosedSetNameAsTheWholeTemplate_NoOtherProperties()
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        reporter.LogEvent(TelemetryEvents.UploadStarted); // the upload_started-style event AC-056-4 names

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("upload_started", entry.RenderedText); // template text IS exactly the closed-set name — App Insights queries by it (TA-10.2)
        Assert.Empty(entry.Args); // no properties at all: upload_started has none in TA-10.2, and correlationId/traceparent arrive via FromLogContext enrichment, not here
    }

    [Theory]
    [InlineData(TelemetryEvents.TransferPageViewed)]
    [InlineData(TelemetryEvents.LoginSuccess)]
    public void LogEvent_EachClosedSetEvent_UsesItsExactWireName(string eventName)
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        reporter.LogEvent(eventName);

        Assert.Equal(eventName, logger.Entries[0].RenderedText); // no renames: wire value == TA-10.2 literal
        Assert.Empty(logger.Entries[0].Args);
    }

    [Fact]
    public void UploadCompleted_UsesTheOnlyContractNamedProperties_BytesDurationMsRetries()
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        reporter.UploadCompleted(123456L, 789L, 2); // upload_completed is the only event TA-10.2 names properties for (line 816)

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("upload_completed 123456 bytes in 789 ms after 2 retries", entry.RenderedText); // pins both the exact template text AND argument order — T-056-05 re-asserts this through real Serilog output
        Assert.Equal(new object[] { 123456L, 789L, 2 }, entry.Args); // bytes, durationMs, retries in that order — M.E.L bakes args into FormattedLogValues; extraction below preserves their original order
    }

    // ---- T-014 (TA-4.2#4): transfer_page_viewed carries the linkId hash as its one property (DECISION B) ----

    [Fact]
    public void TransferPageViewed_EmitsExactNameWithLinkIdHashProperty()
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        reporter.TransferPageViewed("deadbeef"); // the caller passes a PiiTransforms hash — never the raw linkId (TA-9.4 style)

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("transfer_page_viewed deadbeef", entry.RenderedText); // exact template text: the closed-set name + the one {linkId} property (T-014, DECISION B)
        Assert.Equal(new object[] { "deadbeef" }, entry.Args); // the hash is the only property — never the raw 8-char id (TA-9.4 style)
    }

    // ---- Closed-set gate (EC-056-4: ad-hoc names fail here, not silently later in App Insights) ----

    [Fact]
    public void LogEvent_AdHocName_ThrowsBeforeAnythingIsEmitted()
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        Assert.Throws<ArgumentException>(() => reporter.LogEvent("upload_started!")); // a typo must fail fast — "names are code"

        Assert.Empty(logger.Entries); // nothing half-emitted with an ad-hoc name
    }

    [Fact]
    public void LogEvent_MetricName_IsNotAnEvent()
    {
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        Assert.Throws<ArgumentException>(() => reporter.LogEvent(TelemetryEvents.UploadSuccessRate)); // metrics share the closed list but are not events (same pipeline, T-056-04 note)
    }

    // ---- TA-9.4 PII slots live on the reporter ----

    [Fact]
    public void FileNameSlot_ReturnsTheSha256Transform_NotTheRawName() =>
        Assert.Equal("18b7cb099a9ea3f50ba899b5ba81e0d377a5f3b16f8f6eeb8b3e58cd4692b993", new WaTelemetryReporter(new RecordingLogger(), Key).FileNameSlot("a.txt")); // raw "a.txt" never enters a property

    [Fact]
    public void StorageBytesActive_TemplateIsNamePlusBytesProperty()
    {
        // T-007-05 / TA-10.2: storage_bytes_active — App Insights queries by the exact template text (AC-007-2's meter half).
        var logger = new RecordingLogger();
        var reporter = new WaTelemetryReporter(logger, Key);

        reporter.StorageBytesActive(5_368_709_120L); // exactly STORAGE_QUOTA Free — an account at 5 GB of 5 GB reads a full meter

        var entry = Assert.Single(logger.Entries);
        Assert.StartsWith("storage_bytes_active", entry.RenderedText); // the closed name leads the message text — App Insights queries by it (TA-10.2)
        Assert.Equal("storage_bytes_active 5368709120", entry.RenderedText); // exact template text: the name + the byte count rendered, no other properties (like expiry_job_lag_seconds's {seconds})
        Assert.Equal(new object[] { 5_368_709_120L }, entry.Args); // the byte count is the only property — long-typed, no decimals

        // Closed-set gate: storage_bytes_active stays a metric (same pipeline), not an event.
        Assert.False(TelemetryEvents.IsEvent(TelemetryEvents.StorageBytesActive));
        Assert.Equal(30, TelemetryEvents.AllNames().Count); // closed-set count unchanged — matches wa.domain.unit's ConstMembers pin (23 events + 7 metrics)
    }

    [Fact]
    public void IpSlot_ReturnsTheKeyedHmacTransform_NotTheRawIp() =>
        Assert.Equal("0ee7d12715e0fe81319cd148d4b43302ac231095c09642949a3660c8c664faea", new WaTelemetryReporter(new RecordingLogger(), Key).IpSlot("10.0.0.1")); // keyed by the ctor key — same vector as wa.domain.unit's PiiTransforms tests
}

/// <summary>Records exactly what M.E.L hands to a logger: the rendered text + any pre-formatted args (M.E.L's typed LogInformation overloads bake arguments into state via FormattableStringFactory, so this is what Serilog/App Insights also receive).</summary>
internal sealed class RecordingLogger : ILogger<WaTelemetryReporter>
{
    public record Entry(string RenderedText, object?[] Args);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    // Every extension overload funnels into the params overload — one place to record.
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Record(state?.ToString() ?? string.Empty, ExtractArgs(state!));
    public void Log(LogLevel logLevel, EventId eventId, string? message, Exception? exception, params object?[] args) => Record(message ?? string.Empty, args);

    private static object?[] ExtractArgs(object state) =>
        ((state as System.Collections.IEnumerable)?
            .Cast<System.Collections.Generic.KeyValuePair<string, object?>>() // FormattedLogValues enumerates name/value pairs in argument order; {OriginalFormat} last
            .Where(kv => kv.Key != "{OriginalFormat}")
            .Select(kv => (object?)kv.Value).ToArray()) ?? [];

    private void Record(string renderedText, object?[] args) => Entries.Add(new Entry(renderedText, args));
}
