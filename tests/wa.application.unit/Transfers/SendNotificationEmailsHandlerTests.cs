using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Events;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// T-006-01: f-email consumer handler tests.
public class SendNotificationEmailsHandlerTests
{
    private readonly StubEmailStore _store;
    private readonly StubEmailSender _emailSender;
    private readonly StubTemplateRenderer _templateRenderer;
    private readonly StubEventPublisher _eventPublisher;
    private readonly EventDeduplicator _deduplicator;
    private readonly StubTelemetryReporter _telemetry;
    private readonly SendNotificationEmailsHandler _handler;

    public SendNotificationEmailsHandlerTests()
    {
        _store = new StubEmailStore();
        _emailSender = new StubEmailSender();
        _templateRenderer = new StubTemplateRenderer();
        _eventPublisher = new StubEventPublisher();
        _deduplicator = new EventDeduplicator(() => DateTimeOffset.UtcNow);
        _telemetry = new StubTelemetryReporter();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Wa:Email:FromAddress"] = "no-reply@protodrop.com",
                ["Wa:Url:Public"] = "https://protodrop.com"
            })
            .Build();

        _handler = new SendNotificationEmailsHandler(
            _store,
            _emailSender,
            _templateRenderer,
            _eventPublisher,
            _deduplicator,
            _telemetry,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SendNotificationEmailsHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ThreeAddressesInclSender_OwnAddress_ExactlyTwoSends()
    {
        // AC-006-1: 3 addresses incl sender's own → exactly 2 sends.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            "sender@x.com",
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" },
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "b@y.com" },
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "sender@x.com" } // sender's own
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com", "b@y.com", "sender@x.com"]);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(2, _emailSender.Sent.Count); // sender's own address skipped
        Assert.Contains(_emailSender.Sent, m => m.To == "a@x.com");
        Assert.Contains(_emailSender.Sent, m => m.To == "b@y.com");
        Assert.DoesNotContain(_emailSender.Sent, m => m.To == "sender@x.com");
    }

    [Fact]
    public async Task Handle_EmptyEmails_ZeroSends()
    {
        // AC-006-4: emails[] empty → zero sends.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        var envelope = CreateEnvelope(transferId, []);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task Handle_FromAlwaysConfigValue_ReplyToOnlyWhenSenderEmailExists()
    {
        // US-006-04: From = config value always; ReplyTo present only when SenderEmail exists.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            "sender@x.com",
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("no-reply@protodrop.com", _emailSender.Sent[0].From);
        Assert.Equal("sender@x.com", _emailSender.Sent[0].ReplyTo);
    }

    [Fact]
    public async Task Handle_AllRecipientsAlreadyNotified_ZeroSends()
    {
        // EC-006-4: replayed event with non-null NotifiedAtUtc → no second send.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        var notifiedAt = DateTime.UtcNow;
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com", NotifiedAtUtc = notifiedAt },
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "b@y.com", NotifiedAtUtc = notifiedAt }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com", "b@y.com"]);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Empty(_emailSender.Sent); // all already notified
    }

    [Fact]
    public async Task Handle_SuppressedAddressSkipped()
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" },
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "suppressed@x.com" }
        };

        _store.SuppressedAddresses = new HashSet<string> { "suppressed@x.com" };

        var envelope = CreateEnvelope(transferId, ["a@x.com", "suppressed@x.com"]);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("a@x.com", _emailSender.Sent[0].To);
    }

    [Fact]
    public async Task Handle_TemplateContentIncludesFileNamesSizesCtaUrlExpiryDays()
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536), new FileEntry("photo.jpg", 1048576)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        var email = _emailSender.Sent[0];

        // Template content assertions.
        Assert.Contains("report.pdf", email.HtmlBody);
        Assert.Contains("photo.jpg", email.HtmlBody);
        Assert.Contains("1.5 KB", email.HtmlBody); // 1536 bytes
        Assert.Contains("1.0 MB", email.HtmlBody); // 1048576 bytes
        Assert.Contains("https://protodrop.com/t/ABCD1234", email.HtmlBody); // CTA URL
        Assert.Contains("30 days", email.HtmlBody); // Expiry days
    }

    private static EventEnvelope CreateEnvelope(Guid transferId, string[] emails)
    {
        var payload = new
        {
            transferId,
            linkId = "ABCD1234",
            totalBytes = 1024,
            fileCount = 1,
            emails,
            senderEmail = (string?)null,
            scheduledSendAt = (DateTime?)null
        };

        return new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.TransferCreated,
            Version = 1,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            PartitionKey = "ABCD1234",
            Payload = payload
        };
    }

    // ---- Stubs ----

    private sealed class StubEmailStore : IEmailStore
    {
        public EmailTransferContext? Context { get; set; }
        public List<EmailRecipient> Recipients { get; set; } = [];
        public HashSet<string> SuppressedAddresses { get; set; } = new();

        public Task<EmailTransferContext?> GetTransferWithFilesAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult(Context is not null && Context.TransferId == transferId ? Context : null);

        public Task<IReadOnlyList<EmailRecipient>> GetRecipientsAsync(Guid transferId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EmailRecipient>>(Recipients);

        public Task<IReadOnlySet<string>> GetSuppressedAddressesAsync(
            IReadOnlyCollection<string> addresses,
            string? senderEmail,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                SuppressedAddresses.Where(a => addresses.Contains(a)),
                StringComparer.OrdinalIgnoreCase));

        public Task MarkNotifiedAsync(IReadOnlyCollection<Guid> recipientIds, DateTime notifiedAtUtc, CancellationToken ct)
        {
            foreach (var r in Recipients.Where(r => recipientIds.Contains(r.Id)))
                r.NotifiedAtUtc = notifiedAtUtc;
            return Task.CompletedTask;
        }
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

    private sealed class StubTemplateRenderer : ITemplateRenderer
    {
        public (string Subject, string HtmlBody, string TextBody) Render(TemplateData data)
        {
            var html = $"""
                <html>
                    <body>
                        Hi,
                        {data.SenderName} has shared files with you.
                        
                        Files:
                        {string.Join("<br>", data.Files.Select(f => $"{f.Name} ({FormatBytes(f.SizeBytes)})"))}
                        
                        <a href="{data.CtaUrl}">Download files</a>
                        
                        This link expires in {data.ExpiryDays} days.
                    </body>
                </html>
            """;

            var text = $"""
                Hi,
                {data.SenderName} has shared files with you.
                
                Files:
                {string.Join("\n", data.Files.Select(f => $"- {f.Name} ({FormatBytes(f.SizeBytes)})"))}
                
                Download your files:
                {data.CtaUrl}
                
                This link expires in {data.ExpiryDays} days.
            """;

            return ($"Transfer from {data.SenderName}", html, text);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            var units = new[] { "KB", "MB", "GB" };
            var value = (double)bytes;
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }
            return $"{value:F1} {units[unitIndex]}";
        }
    }

    private sealed class StubEventPublisher : IEventPublisher
    {
        public List<EventEnvelope> Published { get; } = [];

        public Task<DomainEventRecord> PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken)
        {
            Published.Add(envelope);
            return Task.FromResult(new DomainEventRecord { EventId = envelope.EventId, IsPublished = true });
        }
    }

    private sealed class StubTelemetryReporter : WaTelemetryReporter
    {
        public List<string> Events { get; } = [];

        public StubTelemetryReporter() : base(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WaTelemetryReporter>.Instance,
            new byte[32])
        {
        }

        public override void LogEvent(string eventName)
        {
            Events.Add(eventName);
            base.LogEvent(eventName);
        }
    }
}
