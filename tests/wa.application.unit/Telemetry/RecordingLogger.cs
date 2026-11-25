using Microsoft.Extensions.Logging;
using wa.application.Telemetry;

namespace wa.application.unit.Telemetry;

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
