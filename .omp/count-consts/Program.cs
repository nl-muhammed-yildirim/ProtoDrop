using System;
using Microsoft.Extensions.Logging;

var logger = new DumpLogger();
logger.LogInformation("upload_completed {bytes} bytes in {durationMs} ms after {retries} retries", 123456L, 789L, 2);

public sealed class DumpLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"type: {state.GetType()}");
        Console.WriteLine($"is IEnumerable<object>: {(state is System.Collections.Generic.IEnumerable<object>)}");
        if (state is System.Collections.IEnumerable en)
            foreach (var v in en) Console.WriteLine($"  elem: {v} ({v?.GetType().Name})");
    }
}
