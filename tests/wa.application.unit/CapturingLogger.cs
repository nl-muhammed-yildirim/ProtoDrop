using Microsoft.Extensions.Logging;

namespace wa.application.unit;

/// <summary>
/// T-012-06 (TA-9.4, EC-012-3): generic capturing logger — records level, rendered text and the structured property values,
/// so facts can prove a log line carries ids and never a raw email / name.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, IReadOnlyList<object?> Values, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var values = new List<object?>();
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var kv in pairs)
            {
                if (kv.Key == "{OriginalFormat}") continue;
                values.Add(kv.Value);
            }
        }

        Entries.Add(new Entry(logLevel, formatter(state, exception), values, exception));
    }

    /// <summary>True when any entry's rendered message or any structured value contains <paramref name="s"/> (ordinal, ignore case).</summary>
    public bool Mentions(string s) => Entries.Any(e =>
        e.Message.Contains(s, StringComparison.OrdinalIgnoreCase)
        || e.Values.Any(v => v?.ToString()?.Contains(s, StringComparison.OrdinalIgnoreCase) == true));
}
