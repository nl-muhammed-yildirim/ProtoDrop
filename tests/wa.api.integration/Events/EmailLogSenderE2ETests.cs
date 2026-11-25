using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using Xunit.Abstractions;
using wa.application.Blob;
using wa.application.Events;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.infrastructure.Email;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration.Events;

/// <summary>
/// T-006-05 (US-006-01 + US-006-03, T-019 exit check): the real API publishes transfer.created, the real f-email handler
/// (composed like src/wa.workers/Program.cs) renders the real templates and sends through the sender picked by the
/// Wa:Email:Sender=log switch; an unsubscribe link taken from a rendered email mutes that sender only.
/// Only the Service Bus hop is skipped (nothing consumes events locally: EmailFunction is Service Bus triggered).
/// </summary>
public class EmailLogSenderE2ETests : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string PublicOrigin = "http://localhost:5173";
    private const string FromAddress = "no-reply@example.com";

    private readonly ITestOutputHelper _output;
    private readonly CapturingLoggerProvider _capture = new();
    private readonly ILoggerFactory _loggerFactory;

    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private string _connString = null!;
    private BlobServiceClient _svc = null!;
    private TestFactory _factory = null!;
    private HttpClient _client = null!;

    public EmailLogSenderE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = new LoggerFactory(new ILoggerProvider[] { _capture });
    }

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", PublicOrigin);
        // Other classes leave a dummy Service Bus connection string in this process-global env: clear it BEFORE the host
        // is built so DI picks InMemoryEventPublisher (TA-0.2 rule 7).
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", null);

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", _connString);

        await using (var migrateDb = NewDb())
            await migrateDb.Database.MigrateAsync();

        _svc = new BlobServiceClient(
            accountUrl,
            new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await _svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await _svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new TestFactory();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        _loggerFactory.Dispose();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Fact A: AC-006-1 / FR-006-2 / FR-006-3 ----

    [Fact]
    public async Task LogSender_ThreeAddressesInclSendersOwn_ExactlyTwoRenderedEmails()
    {
        var transferId = await SetupDraftTransferAsync(("report.pdf", 1536), ("notes.txt", 512));

        using var response = await PostSendAsync(transferId, new
        {
            emails = new[] { "ann@example.com", "ben@example.com", "alice@example.com" },
            senderName = "Alice Example",
            senderEmail = "alice@example.com"
        }, $"key-e2e-a-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var before = _capture.Mails().Count;
        await RunWorkerAsync(TransferCreatedEnvelope(transferId));
        var mails = _capture.Mails().Skip(before).ToList();

        // FR-006-3: sender's own address skipped → exactly 2 mails.
        Assert.Equal(2, mails.Count);
        Assert.Equivalent(new[] { "ann@example.com", "ben@example.com" }, mails.Select(m => m.To));
        Assert.DoesNotContain(mails, m => m.To == "alice@example.com");

        await using var db = NewDb();
        var transfer = await db.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        var expectedDays = (int)Math.Ceiling((transfer.ExpiresAtUtc - DateTime.UtcNow).TotalDays);

        foreach (var mail in mails)
        {
            Assert.Equal("Transfer from Alice Example", mail.Subject);
            Assert.Contains("report.pdf", mail.Html);
            Assert.Contains("1.5 KB", mail.Html);
            Assert.Contains("notes.txt", mail.Html);
            Assert.Contains("512 B", mail.Html);
            Assert.Contains("report.pdf (1.5 KB)", mail.Text);
            Assert.Equal(1, Regex.Count(mail.Html, "Download files"));
            Assert.Equal(1, Regex.Count(mail.Html, Regex.Escape($"href=\"{PublicOrigin}/t/{transfer.LinkId}\"")));
            Assert.Contains($"This link expires in {expectedDays} days.", mail.Html);
            Assert.Equal(1, Regex.Count(mail.Html, "href=\"[^\"]*/unsubscribe/"));
        }

        // DB: ann + ben notified, alice's row exists but was never notified.
        var recipients = await db.Set<EmailRecipient>().AsNoTracking().Where(r => r.TransferId == transferId).ToListAsync();
        Assert.Equal(3, recipients.Count);
        Assert.NotNull(recipients.Single(r => r.Address == "ann@example.com").NotifiedAtUtc);
        Assert.NotNull(recipients.Single(r => r.Address == "ben@example.com").NotifiedAtUtc);
        Assert.Null(recipients.Single(r => r.Address == "alice@example.com").NotifiedAtUtc);

        // D-4: manual visual-check artifact (under bin/, gitignored; never committed).
        var dir = Path.Combine(AppContext.BaseDirectory, "artifacts", "T-006-05");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transfer-email-en.html");
        await File.WriteAllTextAsync(path, mails.First(m => m.To == "ann@example.com").Html);
        _output.WriteLine($"Rendered email artifact: {path}");
    }

    // ---- Fact B: AC-006-3 / FR-006-5 ----

    [Fact]
    public async Task Unsubscribe_FromRenderedLink_MutesThatSenderOnly()
    {
        // Transfer 1: alice -> ben. Take the unsubscribe link out of the rendered email.
        var t1 = await SetupDraftTransferAsync(("one.txt", 512));
        await SendAsync(t1, new[] { "ben@example.com" }, "alice@example.com");
        var before = _capture.Mails().Count;
        await RunWorkerAsync(TransferCreatedEnvelope(t1));
        var first = _capture.Mails().Skip(before).ToList();
        var benMail = Assert.Single(first);
        Assert.Equal("ben@example.com", benMail.To);

        var match = Regex.Match(benMail.Html, "href=\"(?<url>[^\"]*/unsubscribe/(?<token>[^\"]+))\"");
        Assert.True(match.Success, "rendered email has no unsubscribe link");
        Assert.StartsWith(PublicOrigin + "/unsubscribe/", match.Groups["url"].Value);

        using (var unsub = await _client.GetAsync($"/unsubscribe/{match.Groups["token"].Value}"))
            Assert.Equal(HttpStatusCode.OK, unsub.StatusCode);

        await using (var db = NewDb())
        {
            var suppression = await db.Set<EmailSuppression>().AsNoTracking().SingleAsync();
            Assert.Equal("ben@example.com", suppression.Address);
            Assert.Equal("alice@example.com", suppression.SenderEmail);
        }

        // Transfer 2: alice -> ben + ann. Only ann is mailed; ben stays un-notified.
        var t2 = await SetupDraftTransferAsync(("two.txt", 512));
        await SendAsync(t2, new[] { "ben@example.com", "ann@example.com" }, "alice@example.com");
        before = _capture.Mails().Count;
        await RunWorkerAsync(TransferCreatedEnvelope(t2));
        var second = _capture.Mails().Skip(before).ToList();
        Assert.Equal("ann@example.com", Assert.Single(second).To);

        await using (var db = NewDb())
        {
            var benRow = await db.Set<EmailRecipient>().AsNoTracking()
                .SingleAsync(r => r.TransferId == t2 && r.Address == "ben@example.com");
            Assert.Null(benRow.NotifiedAtUtc);
        }

        // Transfer 3: a different sender (bob) -> ben. Mute is per sender, so ben is mailed.
        var t3 = await SetupDraftTransferAsync(("three.txt", 512));
        await SendAsync(t3, new[] { "ben@example.com" }, "bob@example.com");
        before = _capture.Mails().Count;
        await RunWorkerAsync(TransferCreatedEnvelope(t3));
        var third = _capture.Mails().Skip(before).ToList();
        Assert.Equal("ben@example.com", Assert.Single(third).To);
    }

    // ---- Helpers ----

    private WaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);

    private async Task SendAsync(Guid transferId, string[] emails, string senderEmail)
    {
        using var response = await PostSendAsync(transferId, new
        {
            emails,
            senderName = "Alice Example",
            senderEmail
        }, $"key-e2e-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>The real transfer.created envelope the API published (InMemoryEventPublisher), deserialized like EmailFunction does.</summary>
    private EventEnvelope TransferCreatedEnvelope(Guid transferId)
    {
        var publisher = Assert.IsType<InMemoryEventPublisher>(_factory.Services.GetRequiredService<IEventPublisher>());
        var matches = publisher.GetByTopic("core")
            .Select(e => JsonSerializer.Deserialize<EventEnvelope>(e.Json)!)
            .Where(e => e.Type == EventType.TransferCreated)
            .Where(e =>
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(e.Payload));
                return doc.RootElement.TryGetProperty("transferId", out var id) &&
                       id.TryGetGuid(out var g) && g == transferId;
            })
            .ToList();
        return Assert.Single(matches);
    }

    /// <summary>Worker side, composed like src/wa.workers/Program.cs: own DbContext, real renderer, sender from the log switch.</summary>
    private async Task RunWorkerAsync(EventEnvelope envelope)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wa:Url:Public"] = PublicOrigin,
            ["Wa:Jwt:Secret"] = TestSecret,
            ["Wa:Email:FromAddress"] = FromAddress,
            ["Wa:Email:Sender"] = "log"
        }).Build();

        await using var workerDb = NewDb();
        var telemetry = new WaTelemetryReporter(_loggerFactory.CreateLogger<WaTelemetryReporter>(), Encoding.UTF8.GetBytes(TestSecret));
        var handler = new SendNotificationEmailsHandler(
            new EmailStore(workerDb),
            EmailSenderSelector.Create(config, _loggerFactory), // the Wa:Email:Sender=log arm
            new StubbleTemplateRenderer(_loggerFactory.CreateLogger<StubbleTemplateRenderer>(), telemetry),
            new InMemoryEventPublisher(), // worker-side email_sent sink; not asserted
            new EventDeduplicator(() => DateTimeOffset.UtcNow),
            telemetry,
            config,
            _loggerFactory.CreateLogger<SendNotificationEmailsHandler>(),
            new SenderBrandingStore(workerDb),
            new FeaturesProvider(
                new PlansCache(new PlansStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
                new FlagsCache(new FlagsStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow)));
        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
    }

    private async Task<Guid> SetupDraftTransferAsync(params (string Name, long Size)[] files)
    {
        var draftResponse = await _client.PostAsJsonAsync("/api/v1/transfers/draft",
            new { files = files.Select(f => new { name = f.Name, sizeBytes = f.Size, contentType = "text/plain" }).ToArray() });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);

        using var draftDoc = JsonDocument.Parse(await draftResponse.Content.ReadAsByteArrayAsync());
        var draftId = new Guid(draftDoc.RootElement.GetProperty("draftId").GetString()!);
        var fileIds = draftDoc.RootElement.GetProperty("files").EnumerateArray()
            .Select(f => new Guid(f.GetProperty("fileId").GetString()!)).ToList();

        for (var i = 0; i < files.Length; i++)
        {
            var stagingPath = BlobPaths.StagingFile(draftId, fileIds[i]);
            var bytes = new byte[files[i].Size];
            RandomNumberGenerator.Fill(bytes);
            var cut = stagingPath.IndexOf('/');
            using var ms = new MemoryStream(bytes);
            await _svc.GetBlobContainerClient(stagingPath[..cut]).GetBlobClient(stagingPath[(cut + 1)..])
                .UploadAsync(ms, true, default);
        }

        var finalizePayload = JsonSerializer.Serialize(new
        {
            files = files.Select((f, i) => new { fileId = fileIds[i], name = f.Name, sizeBytes = f.Size, contentType = "text/plain", retries = 0 }).ToArray()
        });
        var finalizeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/draft/{draftId:N}/finalize")
        {
            Content = new StringContent(finalizePayload, Encoding.UTF8, "application/json")
        };
        finalizeRequest.Headers.Add("Idempotency-Key", $"key-finalize-{Guid.NewGuid():N}");
        using var finalizeResponse = await _client.SendAsync(finalizeRequest);
        Assert.Equal(HttpStatusCode.Created, finalizeResponse.StatusCode);

        using var finalizeDoc = JsonDocument.Parse(await finalizeResponse.Content.ReadAsByteArrayAsync());
        return new Guid(finalizeDoc.RootElement.GetProperty("id").GetString()!);
    }

    private async Task<HttpResponseMessage> PostSendAsync(Guid transferId, object body, string idempotencyKey)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/transfers/{transferId:N}/send")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _client.SendAsync(request);
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }

    private sealed record Mail(string To, string Subject, string Html, string Text);

    /// <summary>Captures the log sender's own output (LogEmailSender "EMAIL SENT" entries).</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, string Message)> _entries = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose() { }

        public List<Mail> Mails() => _entries
            .Where(e => e.Category == typeof(LogEmailSender).FullName && e.Message.StartsWith("EMAIL SENT"))
            .Select(e => Parse(e.Message))
            .ToList();

        private static Mail Parse(string message)
        {
            var to = Regex.Match(message, @"^To: (.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            var subject = Regex.Match(message, @"^Subject: (.*)$", RegexOptions.Multiline).Groups[1].Value.Trim();
            var htmlStart = message.IndexOf("\n\nHTML:\n", StringComparison.Ordinal) + "\n\nHTML:\n".Length;
            var textMarker = message.IndexOf("\n\nText:\n", StringComparison.Ordinal);
            var html = message[htmlStart..textMarker];
            var text = message[(textMarker + "\n\nText:\n".Length)..];
            return new Mail(to, subject, html, text);
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string Category, string Message)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => sink.Enqueue((category, formatter(state, exception)));
        }
    }
}
