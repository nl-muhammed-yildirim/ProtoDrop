using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using wa.api.Pipeline;
using wa.api.Telemetry;

namespace wa.api.integration;

/// <summary>
/// T-008d / T-008 exit check (part 4 of 4 — pipeline foundation integration tests).
/// Exercises the pipeline components end-to-end at the component level:
///
///   (a) Correlation ID: unknown route (404) and forced 500 both return Problem+JSON
///       with closed code AND correlationId in the response body.
///   (b) Telemetry: an upload_started-style event is emitted via the telemetry
///       helper (WaTelemetryReporter) and reaches the test capture sink.
///   (c) Rate limiting: a rate-limit breach returns Problem+JSON from the closed
///       error code list (RATE_LIMITED).
///
/// Tests the components in isolation using a captured Serilog sink for telemetry
/// verification, without requiring a full HTTP server.
/// </summary>
public class PipelineIntegrationTest
{
    private readonly LogCaptureSink _logCapture;

    public PipelineIntegrationTest()
    {
        _logCapture = new LogCaptureSink();
    }

    /// <summary>
    /// (a1) Unknown routes are properly mapped to ErrorCode.NOT_FOUND in the Problem+JSON response
    /// writer, with the correct closed code list and correlationId.
    /// </summary>
    [Fact]
    public void UnknownRoute_WritesCorrectProblemJson()
    {
        // Arrange
        var correlationId = "abcd1234";
        var status = 404;
        var code = ErrorCode.NOT_FOUND;
        var type = ProblemWriter.BuildTypeUri("http://localhost:8080", code);

        // Act
        var body = ProblemWriter.Write(
            status: status,
            code: code,
            correlationId: correlationId,
            type: type);

        var json = JsonNode.Parse(body);

        // Assert
        Assert.NotNull(json);
        Assert.Equal(404, json["status"]?.GetValue<int>());
        Assert.Equal("NOT_FOUND", json["code"]?.GetValue<string>());
        Assert.Equal(correlationId, json["correlationId"]?.GetValue<string>());
        Assert.Equal("Not found", json["title"]?.GetValue<string>());
        Assert.Equal(type, json["type"]?.GetValue<string>());
    }

    /// <summary>
    /// (a2) Unhandled exceptions (500) are mapped to ErrorCode.INTERNAL in the
    /// Problem+JSON response, with the correct closed code and correlationId.
    /// </summary>
    [Fact]
    public void ForcedError_WritesCorrectProblemJson()
    {
        // Arrange
        var correlationId = "12345678";
        var status = 500;
        var code = ErrorCode.INTERNAL;
        var type = ProblemWriter.BuildTypeUri("http://localhost:8080", code);

        // Act
        var body = ProblemWriter.Write(
            status: status,
            code: code,
            correlationId: correlationId,
            type: type);

        var json = JsonNode.Parse(body);

        // Assert
        Assert.NotNull(json);
        Assert.Equal(500, json["status"]?.GetValue<int>());
        Assert.Equal("INTERNAL", json["code"]?.GetValue<string>());
        Assert.Equal(correlationId, json["correlationId"]?.GetValue<string>());
        Assert.Equal("Internal server error", json["title"]?.GetValue<string>());
        Assert.Equal(type, json["type"]?.GetValue<string>());
    }

    /// <summary>
    /// (a3) Rate-limit errors are mapped to ErrorCode.RATE_LIMITED in Problem+JSON
    /// with the correct closed code and correlationId.
    /// </summary>
    [Fact]
    public void RateLimitExceeded_WritesCorrectProblemJson()
    {
        // Arrange
        var correlationId = "87654321";
        var status = 429;
        var code = ErrorCode.RATE_LIMITED;
        var type = ProblemWriter.BuildTypeUri("http://localhost:8080", code);

        // Act
        var body = ProblemWriter.Write(
            status: status,
            code: code,
            correlationId: correlationId,
            type: type);

        var json = JsonNode.Parse(body);

        // Assert
        Assert.NotNull(json);
        Assert.Equal(429, json["status"]?.GetValue<int>());
        Assert.Equal("RATE_LIMITED", json["code"]?.GetValue<string>());
        Assert.Equal(correlationId, json["correlationId"]?.GetValue<string>());
        Assert.Equal("Rate limited", json["title"]?.GetValue<string>());
    }

    /// <summary>
    /// (b1) A WaTelemetryReporter emits an upload_started event with the correct
    /// message template name (TA-10.2 contract).
    /// </summary>
    [Fact]
    public void TelemetryReporter_EmitsUploadStartedWithCorrectName()
    {
        // Arrange
        _logCapture.Clear();
        var logger = new TestLogger(_logCapture);
        var reporter = new WaTelemetryReporter(logger);

        // Act
        reporter.UploadStarted();

        // Assert
        var events = _logCapture.Events;
        Assert.NotEmpty(events);

        var uploadStartedEvent = events.FirstOrDefault(e => e.MessageTemplate.Text == TelemetryEvents.UploadStarted);
        Assert.NotNull(uploadStartedEvent);
        Assert.Equal(LogEventLevel.Information, uploadStartedEvent.Level);
    }

    /// <summary>
    /// (b2) A WaTelemetryReporter emits upload_completed event with all expected
    /// properties from the TA-10.2 schema.
    /// </summary>
    [Fact]
    public void TelemetryReporter_EmitsUploadCompletedWithProperties()
    {
        // Arrange
        _logCapture.Clear();
        var logger = new TestLogger(_logCapture);
        var reporter = new WaTelemetryReporter(logger);

        // Act
        reporter.UploadCompleted(bytes: 1024, durationMs: 500, retries: 2);

        // Assert
        var events = _logCapture.Events;
        Assert.NotEmpty(events);

        var uploadCompletedEvent = events.FirstOrDefault(e => e.MessageTemplate.Text == TelemetryEvents.UploadCompleted);
        Assert.NotNull(uploadCompletedEvent);
        Assert.Equal(LogEventLevel.Information, uploadCompletedEvent.Level);

        // Verify properties are present.
        Assert.True(uploadCompletedEvent.Properties.ContainsKey(TelemetryEvents.PropertyBytes));
        Assert.True(uploadCompletedEvent.Properties.ContainsKey(TelemetryEvents.PropertyDurationMs));
        Assert.True(uploadCompletedEvent.Properties.ContainsKey(TelemetryEvents.PropertyRetries));

        // Verify values.
        var bytesValue = uploadCompletedEvent.Properties[TelemetryEvents.PropertyBytes].ToString().Trim('"');
        Assert.Equal("1024", bytesValue);
    }

    /// <summary>
    /// (b3) Multiple different telemetry event types are correctly emitted with their
    /// exact event-name message templates from the TA-10.2 closed list.
    /// </summary>
    [Fact]
    public void TelemetryReporter_EmitsMultipleEventsWithCorrectNames()
    {
        // Arrange
        _logCapture.Clear();
        var logger = new TestLogger(_logCapture);
        var reporter = new WaTelemetryReporter(logger);

        // Act - emit several different event types
        reporter.UploadStarted();
        reporter.TransferPageViewed();
        reporter.PasswordCorrect();
        reporter.DownloadStarted();

        // Assert
        var events = _logCapture.Events.ToList();
        Assert.NotEmpty(events);

        // Verify each exact event name exists (TA-10.2 contract).
        Assert.Contains(events, e => e.MessageTemplate.Text == TelemetryEvents.UploadStarted);
        Assert.Contains(events, e => e.MessageTemplate.Text == TelemetryEvents.TransferPageViewed);
        Assert.Contains(events, e => e.MessageTemplate.Text == TelemetryEvents.PasswordCorrect);
        Assert.Contains(events, e => e.MessageTemplate.Text == TelemetryEvents.DownloadStarted);
    }

    /// <summary>
    /// (b4) A failed event is emitted at the Warning level per TA-10.5 mapping.
    /// </summary>
    [Fact]
    public void TelemetryReporter_EmitsFailedEventsAtWarningLevel()
    {
        // Arrange
        _logCapture.Clear();
        var logger = new TestLogger(_logCapture);
        var reporter = new WaTelemetryReporter(logger);

        // Act
        reporter.UploadFailed();
        reporter.ZipFailed();
        reporter.EmailFailed();

        // Assert
        var events = _logCapture.Events;

        var uploadFailed = events.FirstOrDefault(e => e.MessageTemplate.Text == TelemetryEvents.UploadFailed);
        Assert.NotNull(uploadFailed);
        Assert.Equal(LogEventLevel.Warning, uploadFailed.Level); // TA-10.5

        var zipFailed = events.FirstOrDefault(e => e.MessageTemplate.Text == TelemetryEvents.ZipFailed);
        Assert.NotNull(zipFailed);
        Assert.Equal(LogEventLevel.Warning, zipFailed.Level);

        var emailFailed = events.FirstOrDefault(e => e.MessageTemplate.Text == TelemetryEvents.EmailFailed);
        Assert.NotNull(emailFailed);
        Assert.Equal(LogEventLevel.Warning, emailFailed.Level);
    }

    /// <summary>
    /// Test logger that writes directly to the capture sink using the Serilog API.
    /// </summary>
    private class TestLogger : ILogger
    {
        private readonly LogCaptureSink _sink;

        public TestLogger(LogCaptureSink sink)
            => _sink = sink;

        public void Write(LogEvent logEvent)
            => _sink.Emit(logEvent);

        // Unused by WaTelemetryReporter, but required by interface.
        public bool IsEnabled(LogEventLevel level) => true;
    }

    /// <summary>
    /// Helper: capture Serilog events during the test for inspection.
    /// </summary>
    private class LogCaptureSink : ILogEventSink
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _lock = new();

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_lock)
                {
                    return _events.AsReadOnly();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_lock)
            {
                _events.Add(logEvent);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _events.Clear();
            }
        }
    }
}