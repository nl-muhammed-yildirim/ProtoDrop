using Microsoft.Extensions.Logging;
using Xunit;
using wa.application.Ports;
using wa.infrastructure.Email;

namespace wa.workers.unit.Email;

/// <summary>T-006-05: Wa:Email:Sender=log switch picks the log-only sender (TA-0.2 rule 7).</summary>
public class EmailSenderSelectorTests
{
    [Fact]
    public async Task Create_Log_ReturnsLogEmailSender()
    {
        var provider = new CapturingProvider();
        using var factory = new LoggerFactory([provider]);

        var sender = EmailSenderSelector.Create("log", "no-reply@protodrop.com", factory);

        Assert.IsType<LogEmailSender>(sender);
        await sender.SendAsync(
            new EmailMessage("to@x.com", "Subject", "<p>Hi</p>", "Hi", "no-reply@protodrop.com", null),
            CancellationToken.None);
        Assert.Contains(provider.Messages, m => m.StartsWith("EMAIL SENT") && m.Contains("To: to@x.com"));
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => sink.Add(formatter(state, exception));
        }
    }
}
