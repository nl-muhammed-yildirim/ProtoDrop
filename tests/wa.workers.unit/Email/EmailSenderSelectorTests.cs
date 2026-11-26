using Microsoft.Extensions.Configuration;
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

        var sender = EmailSenderSelector.Create(Config(("Wa:Email:Sender", "log"), ("DOTNET_ENVIRONMENT", "Development")), factory);

        Assert.IsType<LogEmailSender>(sender);
        await sender.SendAsync(
            new EmailMessage("to@x.com", "Subject", "<p>Hi</p>", "Hi", "no-reply@protodrop.com", null),
            CancellationToken.None);
        Assert.Contains(provider.Messages, m => m.StartsWith("EMAIL SENT") && m.Contains("To: t****@x.com"));
        Assert.DoesNotContain(provider.Messages, m => m.Contains("to@x.com")); // TA-9.4: the raw address never reaches the log
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Create_Log_OutsideDevelopment_Throws(string? environment)
    {
        using var factory = new LoggerFactory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmailSenderSelector.Create(Config(("Wa:Email:Sender", "log"), ("DOTNET_ENVIRONMENT", environment)), factory));

        Assert.Contains("Wa:Email:Sender", ex.Message);
    }

    [Theory]
    [InlineData("AZURE_FUNCTIONS_ENVIRONMENT", "Development")]
    [InlineData("AZURE_FUNCTIONS_ENVIRONMENT", "Local")]
    [InlineData("DOTNET_ENVIRONMENT", "Development")]
    [InlineData("DOTNET_ENVIRONMENT", "Local")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Development")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Local")]
    public void Create_Log_AllowedEnvironment_ReturnsLogSender(string key, string environment)
    {
        using var factory = new LoggerFactory();

        var sender = EmailSenderSelector.Create(Config(("Wa:Email:Sender", "log"), (key, environment)), factory);

        Assert.IsType<LogEmailSender>(sender);
    }

    [Fact]
    public void EnsureSenderAllowed_NonLogSender_InProduction_DoesNotThrow()
    {
        EmailSenderSelector.EnsureSenderAllowed(
            Config(("Wa:Email:Sender", "acs"), ("DOTNET_ENVIRONMENT", "Production")));
    }

    [Fact]
    public void Create_NonLogWithConnection_ReturnsCommunicationHubSender()
    {
        using var factory = new LoggerFactory();

        var sender = EmailSenderSelector.Create(Config(("Wa:CommunicationHub:Connection", FakeConnection)), factory);

        Assert.IsType<CommunicationHubEmailSender>(sender);
    }

    [Fact]
    public void Create_ExplicitNonLogSwitch_WithConnection_ReturnsCommunicationHubSender()
    {
        using var factory = new LoggerFactory();

        var sender = EmailSenderSelector.Create(
            Config(("Wa:Email:Sender", "acs"), ("Wa:CommunicationHub:Connection", FakeConnection)), factory);

        Assert.IsType<CommunicationHubEmailSender>(sender);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NonLogWithoutConnection_ThrowsNamingKey(string? connection)
    {
        using var factory = new LoggerFactory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmailSenderSelector.Create(Config(("Wa:CommunicationHub:Connection", connection)), factory));

        Assert.Contains("Wa:CommunicationHub:Connection", ex.Message);
    }

    private const string FakeConnection =
        "endpoint=https://fake.communication.azure.com/;accesskey=ZmFrZS1hY2Nlc3Mta2V5";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

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
