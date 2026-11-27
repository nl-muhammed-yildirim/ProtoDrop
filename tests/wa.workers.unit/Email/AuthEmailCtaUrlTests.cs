using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.workers.unit.Email;

/// <summary>
/// CHORE-magic-link-redeem (FR-008-1, TA-9.1): the emailed CTA carries the raw base64 token URL-encoded, so
/// '+', '/' and '=' survive the query string and the web redeem page can POST the exact token back.
/// Lives in wa.workers.unit because the real Emails/templates/** are copied next to this test binary.
/// </summary>
public class AuthEmailCtaUrlTests
{
    private const string Token = "ab+cd/ef==";
    private const string EncodedCta = "https://app.example.com/auth?ml=ab%2Bcd%2Fef%3D%3D";

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Url:Public"] = "https://app.example.com" })
        .Build();

    private static EventEnvelope Envelope(object payload) => new()
    {
        EventId = Guid.NewGuid(),
        Type = EventType.MagicLinkRequested,
        Version = 1,
        OccurredAtUtc = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid(),
        PartitionKey = "u@example.com",
        Payload = payload,
    };

    private static void AssertEncoded(StubEmailSender sender)
    {
        var sent = Assert.Single(sender.Sent);
        Assert.Contains(EncodedCta, sent.HtmlBody);
        Assert.Contains(EncodedCta, sent.TextBody);
        Assert.DoesNotContain("ml=ab+cd", sent.HtmlBody);
        Assert.DoesNotContain("ml=ab+cd", sent.TextBody);
    }

    [Fact]
    public async Task MagicLink_CtaUrl_IsUrlEncoded()
    {
        var sender = new StubEmailSender();
        var handler = new SendMagicLinkEmailHandler(
            sender, new RecordingPublisher(), Config(),
            new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[] { 0 }),
            NullLogger<SendMagicLinkEmailHandler>.Instance, TimeProvider.System);

        await handler.HandleAsync(
            Envelope(new { email = "u@example.com", token = Token, expiresAtUtc = DateTime.UtcNow.AddMinutes(10) }),
            1, 5, default);

        AssertEncoded(sender);
    }

    [Fact]
    public async Task ForgotPassword_CtaUrl_IsUrlEncoded()
    {
        var sender = new StubEmailSender();
        var handler = new SendForgotPasswordEmailHandler(
            sender, new RecordingPublisher(), Config(),
            new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[] { 0 }),
            NullLogger<SendForgotPasswordEmailHandler>.Instance, TimeProvider.System);

        await handler.HandleAsync(
            Envelope(new { email = "u@example.com", token = Token, expiresAtUtc = DateTime.UtcNow.AddMinutes(10), purpose = 2 }),
            1, 5, default);

        AssertEncoded(sender);
    }

    private sealed class StubEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
            => Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
    }
}
