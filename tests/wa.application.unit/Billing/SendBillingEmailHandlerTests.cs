using System.Text.Json;
using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-04 (D-7, FR-019-7, D-04): SendBillingEmailHandler — f-email billing_payment_failed, incl. retry/DLQ path.</summary>
public class SendBillingEmailHandlerTests : IDisposable
{
    private const string Email = "payer@example.com";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wa-billing-tpl-" + Guid.NewGuid().ToString("n"));

    public SendBillingEmailHandlerTests()
    {
        var dir = Path.Combine(_root, "billing-payment-failed");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "en.html"), "<p>Reminder {{Step}} of 3</p><a href=\"{{CtaUrl}}\">Update</a>");
        File.WriteAllText(Path.Combine(dir, "en.txt"), "Reminder {{Step}} of 3: {{CtaUrl}}");

        var graceDir = Path.Combine(_root, "billing-grace-ended");
        Directory.CreateDirectory(graceDir);
        File.WriteAllText(Path.Combine(graceDir, "en.html"), "<p>Back on Free</p><a href=\"{{CtaUrl}}\">Plans</a>");
        File.WriteAllText(Path.Combine(graceDir, "en.txt"), "Back on Free: {{CtaUrl}}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class Rig
    {
        public StubEmailSender Sender { get; } = new();
        public RecordingPublisher Events { get; } = new();
        public CapturingLogger<SendBillingEmailHandler> Log { get; } = new();

        public SendBillingEmailHandler Handler(string root, string? from = null)
        {
            var settings = new Dictionary<string, string?> { ["Wa:Url:Public"] = "https://app.example.com" };
            if (from is not null) settings["Wa:Email:FromAddress"] = from;
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new SendBillingEmailHandler(
                Sender, Events, config, new WaTelemetryReporter(new CapturingLogger<WaTelemetryReporter>(), [1, 2, 3]), Log, root, TimeProvider.System);
        }
    }

    private static EventEnvelope Envelope(int step = 1, string? kind = "billing_payment_failed", string? email = Email) => new()
    {
        EventId = Guid.NewGuid(),
        Type = EventType.BillingEmailRequested,
        Version = 1,
        OccurredAtUtc = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid(),
        PartitionKey = Guid.NewGuid().ToString("n"),
        Payload = new { userId = Guid.NewGuid(), email, subscriptionId = Guid.NewGuid(), kind, step }
    };

    [Theory]
    [InlineData(1, "Your ProtoDrop payment didn't go through")]
    [InlineData(2, "Reminder: your ProtoDrop payment still needs attention")]
    [InlineData(3, "Last reminder: update your card to keep ProtoDrop Pro")]
    public async Task Sends_ToPayloadEmail_WithStepSubject_AndCtaUrl_AndEmailSent(int step, string subject)
    {
        var rig = new Rig();
        await rig.Handler(_root, from: "billing@example.com").HandleAsync(Envelope(step), 1, 5, default);

        var sent = Assert.Single(rig.Sender.Sent);
        Assert.Equal(Email, sent.To);
        Assert.Equal("billing@example.com", sent.From);
        Assert.Equal(subject, sent.Subject);
        Assert.Contains($"Reminder {step} of 3", sent.HtmlBody);
        Assert.Contains("https://app.example.com/account", sent.HtmlBody);
        Assert.Contains("https://app.example.com/account", sent.TextBody);

        var env = Assert.Single(rig.Events.Published);
        Assert.Equal(EventType.EmailSent, env.Type);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("billing_payment_failed", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(step, doc.RootElement.GetProperty("step").GetInt32());
    }

    [Fact]
    public async Task FromAddress_DefaultsWhenUnset()
    {
        var rig = new Rig();
        await rig.Handler(_root).HandleAsync(Envelope(), 1, 5, default);

        Assert.Equal("no-reply@protodrop.com", Assert.Single(rig.Sender.Sent).From);
    }

    [Fact]
    public async Task SenderThrows_AtAttempt2Of5_Rethrows_NoEmailFailed()
    {
        var rig = new Rig();
        rig.Sender.ThrowOnSend = new InvalidOperationException("outage");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handler(_root).HandleAsync(Envelope(), 2, 5, default));

        Assert.DoesNotContain(rig.Events.Published, e => e.Type == EventType.EmailFailed);
    }

    [Fact]
    public async Task SenderThrows_AtAttempt5Of5_EmitsEmailFailed_ThenRethrows()
    {
        var rig = new Rig();
        rig.Sender.ThrowOnSend = new InvalidOperationException("outage");

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handler(_root).HandleAsync(Envelope(), 5, 5, default));

        var env = Assert.Single(rig.Events.Published);
        Assert.Equal(EventType.EmailFailed, env.Type);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("billing_payment_failed", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("attempt").GetInt32());
    }

    [Fact]
    public async Task UnknownKind_NoSend()
    {
        var rig = new Rig();
        await rig.Handler(_root).HandleAsync(Envelope(kind: "grace_ending"), 1, 5, default);

        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public async Task MissingEmail_NoSend()
    {
        var rig = new Rig();
        await rig.Handler(_root).HandleAsync(Envelope(email: null), 1, 5, default);

        Assert.Empty(rig.Sender.Sent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task StepOutOfRange_NoSend(int step)
    {
        var rig = new Rig();
        await rig.Handler(_root).HandleAsync(Envelope(step), 1, 5, default);

        Assert.Empty(rig.Sender.Sent);
    }

    [Fact]
    public void MissingTemplates_CtorThrows()
    {
        var rig = new Rig();
        Assert.Throws<InvalidOperationException>(() => rig.Handler(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void MissingGraceTemplates_CtorThrows()
    {
        var root = Path.Combine(Path.GetTempPath(), "wa-billing-tpl-" + Guid.NewGuid().ToString("n"));
        try
        {
            var dir = Path.Combine(root, "billing-payment-failed");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "en.html"), "x");
            File.WriteAllText(Path.Combine(dir, "en.txt"), "x");

            var rig = new Rig();
            Assert.Throws<InvalidOperationException>(() => rig.Handler(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GraceEnded_Sends_FreeSubject_AndCtaUrl_AndEmailSentWithKind()
    {
        var rig = new Rig();
        await rig.Handler(_root).HandleAsync(Envelope(kind: "billing_grace_ended"), 1, 5, default);

        var sent = Assert.Single(rig.Sender.Sent);
        Assert.Equal(Email, sent.To);
        Assert.Equal("You're back on ProtoDrop Free", sent.Subject);
        Assert.Contains("Back on Free", sent.HtmlBody);
        Assert.Contains("https://app.example.com/account", sent.HtmlBody);
        Assert.Contains("https://app.example.com/account", sent.TextBody);

        var env = Assert.Single(rig.Events.Published);
        Assert.Equal(EventType.EmailSent, env.Type);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("billing_grace_ended", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task GraceEnded_SenderThrows_AtLastAttempt_EmitsEmailFailedWithKind_ThenRethrows()
    {
        var rig = new Rig();
        rig.Sender.ThrowOnSend = new InvalidOperationException("outage");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Handler(_root).HandleAsync(Envelope(kind: "billing_grace_ended"), 5, 5, default));

        var env = Assert.Single(rig.Events.Published);
        Assert.Equal(EventType.EmailFailed, env.Type);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(env.Payload));
        Assert.Equal("billing_grace_ended", doc.RootElement.GetProperty("kind").GetString());
    }

    private sealed class StubEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public Exception? ThrowOnSend { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            if (ThrowOnSend is not null)
                throw ThrowOnSend;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }
}
