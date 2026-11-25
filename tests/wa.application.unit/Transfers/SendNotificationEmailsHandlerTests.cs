using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Events;
using wa.application.Limits;
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
    private readonly WaTelemetryReporter _telemetry;
    private readonly CapturingLogger<SendNotificationEmailsHandler> _log = new();
    private readonly SendNotificationEmailsHandler _handler;
    private readonly StubSenderBrandingStore _branding = new();

    public SendNotificationEmailsHandlerTests()
    {
        _store = new StubEmailStore();
        _emailSender = new StubEmailSender();
        _templateRenderer = new StubTemplateRenderer();
        _eventPublisher = new StubEventPublisher();
        _deduplicator = new EventDeduplicator(() => DateTimeOffset.UtcNow);
        _telemetry = new WaTelemetryReporter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WaTelemetryReporter>.Instance,
            new byte[32]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
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
            _log,
            _branding,
            new StubFeaturesProvider());
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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

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
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        var email = _emailSender.Sent[0];

        // Template content assertions.
        Assert.Contains("report.pdf", email.HtmlBody);
        Assert.Contains("photo.jpg", email.HtmlBody);
        // Check for file sizes (1536 bytes = 1.5 KB, 1048576 bytes = 1.0 MB)
        Assert.Contains("1.5", email.HtmlBody);
        Assert.Contains("1.0", email.HtmlBody);
        Assert.Contains("https://protodrop.com/t/ABCD1234", email.HtmlBody); // CTA URL
        Assert.Contains("30 days", email.HtmlBody); // Expiry days
    }

    [Fact]
    public async Task Handle_TransientFailure_NonLastAttempt_ThrowsForRedelivery()
    {
        // AC-006-2 (a): 429 on a non-final attempt → throw to redeliver, no email.failed yet.
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
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }
        };

        _emailSender.ThrowOnSend = new Exception("429");
        var envelope = CreateEnvelope(transferId, ["a@x.com"]);

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(envelope, 1, 5, CancellationToken.None));

        // No email.failed on non-final attempts; nothing was sent.
        Assert.DoesNotContain(_eventPublisher.Published, e => e.Type == EventType.EmailFailed);
        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task Handle_SendFailure_ErrorLog_CarriesRecipientId_NotAddress()
    {
        // T-012-06 (AC-012-3 / TA-9.4): the failure log line identifies the recipient row, never the address.
        var transferId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = recipientId, TransferId = transferId, Address = "a@x.com" }
        };
        _emailSender.ThrowOnSend = new Exception("429");
        var envelope = CreateEnvelope(transferId, ["a@x.com"]);

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(envelope, 1, 5, CancellationToken.None));

        var error = Assert.Single(_log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(recipientId, error.Values);
        Assert.False(_log.Mentions("a@x.com"));
    }

    [Fact]
    public async Task Handle_FinalAttemptFailure_EmitsEmailFailed_ThenThrows()
    {
        // AC-006-2 (b): final attempt (attempt == maxDeliveryCount) → email.failed emitted, then throw to dead-letter.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("file.txt", 1024)]);

        var recipientAddress = "a@x.com";
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = recipientAddress }
        };

        _emailSender.ThrowOnSend = new Exception("429");
        var envelope = CreateEnvelope(transferId, [recipientAddress]);

        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(envelope, 5, 5, CancellationToken.None));

        // Exactly one email.failed with the right payload.
        var failed = _eventPublisher.Published.Where(e => e.Type == EventType.EmailFailed).ToList();
        Assert.Single(failed);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(failed[0].Payload));
        var root = doc.RootElement;
        Assert.Equal(transferId, root.GetProperty("transferId").GetGuid());
        Assert.Equal(recipientAddress, root.GetProperty("to").GetString());
        Assert.Equal(5, root.GetProperty("attempt").GetInt32());
        Assert.Equal("Exception", root.GetProperty("reason").GetString()); // ex.GetType().Name — no Azure SDK dep in wa.application
    }

    [Fact]
    public async Task Handle_TransientFirstAttemptFailure_SecondDelivery_SucceedsWithAttempt2()
    {
        // AC-006-2 (c): transient first-attempt failure → success on second delivery with attempt = 2.
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
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);

        // First attempt fails (records the eventId in the dedup window).
        _emailSender.ThrowOnSend = new Exception("429");
        await Assert.ThrowsAsync<Exception>(() => _handler.HandleAsync(envelope, 1, 5, CancellationToken.None));

        // Second delivery: fresh envelope (new EventId — real redeliveries carry the same eventId but the dedup window is a fast-path optimization; NotifiedAtUtc guard is the guarantee).
        var secondEnvelope = CreateEnvelope(transferId, ["a@x.com"]);
        _emailSender.ThrowOnSend = null;
        await _handler.HandleAsync(secondEnvelope, 2, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        var sent = _eventPublisher.Published.Where(e => e.Type == EventType.EmailSent).ToList();
        Assert.Single(sent);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(sent[0].Payload));
        Assert.Equal(2, doc.RootElement.GetProperty("attempt").GetInt32());
    }

    [Fact]
    public async Task Handle_RecipientWithPreferredLanguage_de_GetsGermanLocalePassedToRenderer()
    {
        // T-006-04: recipient with PreferredLanguage="de" → renderer called with locale "de".
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com", PreferredLanguage = "de" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("de", _templateRenderer.LastLocale);
    }

    [Fact]
    public async Task Handle_RecipientWithoutPreferredLanguage_GetsEnglishDefault()
    {
        // T-006-04: recipient with null PreferredLanguage → renderer called with locale "en".
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" } // PreferredLanguage is null
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("en", _templateRenderer.LastLocale);
    }

    [Fact]
    public async Task Handle_RecipientWithPreferredLanguage_de_EmailSentPayloadContainsLocaleDe()
    {
        // T-014-04 / TA-10.2: email_sent payload carries the locale used (per ADR-011).
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com", PreferredLanguage = "de" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("de", _templateRenderer.LastLocale);

        // TA-10.2: email_sent carries the locale used.
        var sent = _eventPublisher.Published.Where(e => e.Type == EventType.EmailSent).ToList();
        Assert.Single(sent);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(sent[0].Payload));
        Assert.Equal("de", doc.RootElement.GetProperty("locale").GetString());
    }

    [Fact]
    public async Task Handle_MixedLocales_NlAndTr_EachRecipientGetsOwnLanguage()
    {
        // AC (US-014-03 gherkin): 2-recipient send with locales "nl" and "tr" → each gets their own language.
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536)]);

        var nlRecipient = Guid.NewGuid();
        var trRecipient = Guid.NewGuid();
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = nlRecipient, TransferId = transferId, Address = "nl@x.com", PreferredLanguage = "nl" },
            new() { Id = trRecipient, TransferId = transferId, Address = "tr@y.com", PreferredLanguage = "tr" }
        };

        var envelope = CreateEnvelope(transferId, ["nl@x.com", "tr@y.com"]);
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Equal(2, _emailSender.Sent.Count);
        // Per-recipient selection: renderer called once per recipient with their resolved locale.
        Assert.Equal(["nl", "tr"], _templateRenderer.LocalesInOrder);

        // Each email_sent payload carries its own recipient's locale (TA-10.2).
        var sent = _eventPublisher.Published.Where(e => e.Type == EventType.EmailSent).ToList();
        Assert.Equal(2, sent.Count);
        using var docNl = JsonDocument.Parse(JsonSerializer.Serialize(sent[0].Payload));
        Assert.Equal("nl@x.com", docNl.RootElement.GetProperty("to").GetString());
        Assert.Equal("nl", docNl.RootElement.GetProperty("locale").GetString());
        using var docTr = JsonDocument.Parse(JsonSerializer.Serialize(sent[1].Payload));
        Assert.Equal("tr@y.com", docTr.RootElement.GetProperty("to").GetString());
        Assert.Equal("tr", docTr.RootElement.GetProperty("locale").GetString());
    }

    [Fact]
    public async Task Handle_RecipientWithUnknownPreferredLanguage_FallsBackToEn()
    {
        // EC-014-4: unknown locale → en template (no failure, no mixed-language body).
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId,
            "Alice",
            null,
            DateTime.UtcNow.AddDays(30),
            "ABCD1234",
            [new FileEntry("report.pdf", 1536)]);

        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com", PreferredLanguage = "jp" }
        };

        var envelope = CreateEnvelope(transferId, ["a@x.com"]);
        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
        Assert.Equal("en", _templateRenderer.LastLocale); // resolver falls back to en (not "jp")
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

    // ---- T-006-05: per-recipient unsubscribe URL (FR-006-2 / FR-006-5) ----

    private SendNotificationEmailsHandler CreateHandler(string? jwtSecret)
    {
        var values = new Dictionary<string, string?>
        {
            ["Wa:Email:FromAddress"] = "no-reply@protodrop.com",
            ["Wa:Url:Public"] = "https://protodrop.com"
        };
        if (jwtSecret is not null)
            values["Wa:Jwt:Secret"] = jwtSecret;

        return new SendNotificationEmailsHandler(
            _store,
            _emailSender,
            _templateRenderer,
            _eventPublisher,
            _deduplicator,
            _telemetry,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SendNotificationEmailsHandler>.Instance,
            _branding,
            new StubFeaturesProvider());
    }

    private EventEnvelope ArrangeTwoRecipients(string? senderEmail)
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId, "Alice", senderEmail, DateTime.UtcNow.AddDays(30), "ABCD1234", [new FileEntry("file.txt", 1024)]);
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" },
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "b@y.com" }
        };
        return CreateEnvelope(transferId, ["a@x.com", "b@y.com"]);
    }

    [Fact]
    public async Task Handle_SenderEmailAndSecret_EachRecipientGetsOwnVerifiableUnsubscribeUrl()
    {
        var envelope = ArrangeTwoRecipients("sender@x.com");
        var handler = CreateHandler("unit-secret");

        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Equal(2, _templateRenderer.DataInOrder.Count);
        const string prefix = "https://protodrop.com/unsubscribe/";
        var key = System.Text.Encoding.UTF8.GetBytes("unit-secret");
        for (var i = 0; i < 2; i++)
        {
            var url = _templateRenderer.DataInOrder[i].UnsubscribeUrl;
            Assert.NotNull(url);
            Assert.StartsWith(prefix, url);
            var verified = UnsubscribeToken.Verify(url![prefix.Length..], key);
            Assert.NotNull(verified);
            Assert.Equal(_emailSender.Sent[i].To, verified!.Value.Address);
            Assert.Equal("sender@x.com", verified.Value.SenderEmail);
        }
        Assert.NotEqual(_templateRenderer.DataInOrder[0].UnsubscribeUrl, _templateRenderer.DataInOrder[1].UnsubscribeUrl);
    }

    [Fact]
    public async Task Handle_NoSenderEmail_UnsubscribeUrlNull()
    {
        var envelope = ArrangeTwoRecipients(null);
        var handler = CreateHandler("unit-secret");

        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Equal(2, _templateRenderer.DataInOrder.Count);
        Assert.All(_templateRenderer.DataInOrder, d => Assert.Null(d.UnsubscribeUrl));
    }

    [Fact]
    public async Task Handle_NoSecretConfigured_UnsubscribeUrlNull()
    {
        var envelope = ArrangeTwoRecipients("sender@x.com");

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None); // fixture config has no Wa:Jwt:Secret

        Assert.Equal(2, _templateRenderer.DataInOrder.Count);
        Assert.All(_templateRenderer.DataInOrder, d => Assert.Null(d.UnsubscribeUrl));
    }

    // ---- US-021-02: org branding in the notification email ----

    private EventEnvelope ArrangeBrandingTransfer(Guid? ownerId, string senderName = "Anna K.")
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId, senderName, null, DateTime.UtcNow.AddDays(30), "ABCD1234", [new FileEntry("file.txt", 1024)], ownerId);
        _store.Recipients = new List<EmailRecipient>
        {
            new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }
        };
        return CreateEnvelope(transferId, ["a@x.com"]);
    }

    [Fact]
    public async Task Handle_PerTransferNameWithOrg_SubjectOrgThenPerson() // AC-021-2
    {
        var envelope = ArrangeBrandingTransfer(Guid.NewGuid(), "Anna");
        _branding.Row = new SenderBrandingRow("pro", "Studio Nova", null);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        var sent = Assert.Single(_emailSender.Sent);
        Assert.Equal("Transfer from Studio Nova — Anna", sent.Subject);
    }

    [Fact]
    public async Task Handle_ProOwnerWithOrg_SubjectAndBodyShowOrg()
    {
        var envelope = ArrangeBrandingTransfer(Guid.NewGuid());
        _branding.Row = new SenderBrandingRow("pro", "Studio Nova", null);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Equal("Studio Nova — Anna K.", _templateRenderer.DataInOrder[0].SenderName);
        var sent = Assert.Single(_emailSender.Sent);
        Assert.Equal("Transfer from Studio Nova — Anna K.", sent.Subject);
        Assert.Contains("Studio Nova", sent.HtmlBody);
        Assert.Equal("no-reply@protodrop.com", sent.From);
    }

    [Fact]
    public async Task Handle_FreeOwnerWithOrg_SubjectPlain()
    {
        var envelope = ArrangeBrandingTransfer(Guid.NewGuid());
        _branding.Row = new SenderBrandingRow("free", "Studio Nova", null);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        var sent = Assert.Single(_emailSender.Sent);
        Assert.Equal("Transfer from Anna K.", sent.Subject);
        Assert.DoesNotContain("Studio Nova", sent.HtmlBody);
    }

    [Fact]
    public async Task Handle_GuestTransfer_BrandingStoreNotCalled()
    {
        var envelope = ArrangeBrandingTransfer(null);
        _branding.Row = new SenderBrandingRow("pro", "Studio Nova", null);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);

        Assert.Equal(0, _branding.Calls);
        Assert.Equal("Transfer from Anna K.", Assert.Single(_emailSender.Sent).Subject);
    }

    [Fact]
    public async Task Handle_OrgChangedAfterDispatch_SentEmailUnchanged()
    {
        var envelope = ArrangeBrandingTransfer(Guid.NewGuid());
        _branding.Row = new SenderBrandingRow("pro", "Studio Nova", null);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
        _branding.Row = new SenderBrandingRow("pro", "Other Org", null);

        Assert.Contains("Studio Nova", Assert.Single(_emailSender.Sent).Subject);
    }

    // ---- US-022-04: scheduled-send notify gate (FR-022-4, AC-022-1, AC-022-5, EC-022-2, EC-022-3) ----

    private static readonly DateTime ScheduledDue = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);

    private Guid ArrangeScheduled(byte status, params string[] addresses)
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId, "Alice", null, DateTime.UtcNow.AddDays(30), "ABCD1234",
            [new FileEntry("file.txt", 1024)], null, status, ScheduledDue);
        _store.Recipients = addresses
            .Select(a => new EmailRecipient { Id = Guid.NewGuid(), TransferId = transferId, Address = a })
            .ToList();
        return transferId;
    }

    [Fact]
    public async Task Handle_ScheduledStatus_NoSend_NotMarked_NoEmailSentEvent()
    {
        var transferId = ArrangeScheduled(TransferStatus.Scheduled, "a@x.com");

        await _handler.HandleAsync(CreateEnvelope(transferId, ["a@x.com"]), 1, 5, CancellationToken.None);

        Assert.Empty(_emailSender.Sent);
        Assert.All(_store.Recipients, r => Assert.Null(r.NotifiedAtUtc));
        Assert.DoesNotContain(_eventPublisher.Published, e => e.Type == EventType.EmailSent);
    }

    [Fact]
    public async Task Handle_ScheduledRow_Redelivered_StillSkipped_ThenSendsAfterActive()
    {
        var transferId = ArrangeScheduled(TransferStatus.Scheduled, "a@x.com");
        var envelope = CreateEnvelope(transferId, ["a@x.com"]);

        await _handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
        await _handler.HandleAsync(envelope, 2, 5, CancellationToken.None);
        Assert.Empty(_emailSender.Sent);

        // The timer flips the row; the same EventId must not have been recorded by dedup while skipped.
        _store.Context = _store.Context! with { Status = TransferStatus.Active };
        await _handler.HandleAsync(envelope, 3, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
    }

    [Fact]
    public async Task Handle_ScheduledThenExpired_NoSend()
    {
        var transferId = ArrangeScheduled(TransferStatus.Expired, "a@x.com");

        await _handler.HandleAsync(CreateEnvelope(transferId, ["a@x.com"]), 1, 5, CancellationToken.None);

        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task Handle_ScheduledThenDeleted_NoSend()
    {
        var transferId = ArrangeScheduled(TransferStatus.Deleted, "a@x.com");

        await _handler.HandleAsync(CreateEnvelope(transferId, ["a@x.com"]), 1, 5, CancellationToken.None);

        Assert.Empty(_emailSender.Sent);
    }

    [Fact]
    public async Task Handle_ActivatedScheduled_SuppressedAddressSkipped()
    {
        var transferId = ArrangeScheduled(TransferStatus.Active, "a@x.com", "suppressed@x.com");
        _store.SuppressedAddresses = new HashSet<string> { "suppressed@x.com" };

        await _handler.HandleAsync(CreateEnvelope(transferId, ["a@x.com", "suppressed@x.com"]), 1, 5, CancellationToken.None);

        Assert.Equal("a@x.com", Assert.Single(_emailSender.Sent).To);
    }

    [Theory]
    [InlineData(TransferStatus.Active)]
    [InlineData(TransferStatus.Scheduled)] // a null schedule keeps the legacy path regardless of status
    public async Task Handle_ImmediateSend_NoSchedule_StatusIgnored_Sends(byte status)
    {
        var transferId = Guid.NewGuid();
        _store.Context = new EmailTransferContext(
            transferId, "Alice", null, DateTime.UtcNow.AddDays(30), "ABCD1234",
            [new FileEntry("file.txt", 1024)], null, status, null);
        _store.Recipients = [new() { Id = Guid.NewGuid(), TransferId = transferId, Address = "a@x.com" }];

        await _handler.HandleAsync(CreateEnvelope(transferId, ["a@x.com"]), 1, 5, CancellationToken.None);

        Assert.Single(_emailSender.Sent);
    }

    // ---- Stubs ----

    private sealed class StubSenderBrandingStore : ISenderBrandingStore
    {
        public SenderBrandingRow? Row { get; set; }
        public int Calls { get; private set; }

        public Task<SenderBrandingRow?> GetAsync(Guid ownerAppUserId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Row);
        }
    }

    private sealed class StubFeaturesProvider : IFeaturesProvider
    {
        public Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanFeatures { Branding = planCode is "pro" or "business" });
    }

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

        /// <summary>If non-null, SendAsync throws it before recording the send (simulates 429/5xx).</summary>
        public Exception? ThrowOnSend { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            if (ThrowOnSend is not null)
                throw ThrowOnSend;
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTemplateRenderer : ITemplateRenderer
    {
        public string? LastLocale { get; private set; }
        /// <summary>T-014-04: every locale the renderer was called with, in send order (per-recipient verification).</summary>
        public List<string> LocalesInOrder { get; } = [];
        /// <summary>T-006-05: every TemplateData the renderer received, in send order.</summary>
        public List<TemplateData> DataInOrder { get; } = [];

        public (string Subject, string HtmlBody, string TextBody) Render(TemplateData data, string locale)
        {
            LastLocale = locale;
            LocalesInOrder.Add(locale);
            DataInOrder.Add(data);

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

}
