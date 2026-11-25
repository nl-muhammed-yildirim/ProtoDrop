using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using wa.application.Events;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.infrastructure.Email;
using wa.infrastructure.Events;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-001 US-021-02: org branding in the f-email notification (AC-021-1 email half, free-tier case).
/// Real EmailStore, SenderBrandingStore, FeaturesProvider and Stubble renderer; capturing IEmailSender. feature.branding is seeded off, so it is flipped on first.
/// </summary>
public class EmailBrandingIntegrationTests : IAsyncLifetime
{
    private const string FromAddress = "no-reply@protodrop.com";

    private MsSqlContainer _sql = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(_options);
        await _db.Database.MigrateAsync();

        var flag = await _db.Set<FeatureFlag>().SingleAsync(f => f.Key == "feature.branding");
        flag.Value = "true";
        flag.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    [Fact]
    public async Task ProOwnerWithOrg_EmailSubjectAndHeaderShowOrg()
    {
        var sent = await DispatchAsync("pro", "Studio Nova");

        Assert.Equal("Transfer from Studio Nova — Anna K.", sent.Subject);
        Assert.Contains("Studio Nova", sent.HtmlBody);
        Assert.Contains("Anna K.", sent.HtmlBody);
        Assert.Contains("Studio Nova", sent.TextBody);
        Assert.Contains("Anna K.", sent.TextBody);
        Assert.Equal(FromAddress, sent.From); // D-04: From stays the configured no-reply address.
    }

    [Fact]
    public async Task FreeOwnerWithOrg_EmailSubjectPlain()
    {
        var sent = await DispatchAsync("free", "Studio Nova");

        Assert.Equal("Transfer from Anna K.", sent.Subject);
        Assert.DoesNotContain("Studio Nova", sent.HtmlBody);
        Assert.DoesNotContain("Studio Nova", sent.TextBody);
    }

    private async Task<EmailMessage> DispatchAsync(string planCode, string orgName)
    {
        var planId = await _db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"email-branding-{Guid.NewGuid():N}@example.com",
            DisplayName = "Alice",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow,
            OrgName = orgName
        };
        var transferId = Guid.NewGuid();
        _db.Set<AppUser>().Add(owner);
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = "abcdefgh",
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 5,
            DownloadsCount = 0,
            SenderName = "Anna K.",
            OwnerAppUserId = owner.Id,
            TotalBytes = 0,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.Set<EmailRecipient>().Add(new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            Address = "recipient@example.com"
        });
        await _db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wa:Url:Public"] = "http://localhost:5173",
            ["Wa:Email:FromAddress"] = FromAddress
        }).Build();

        await using var workerDb = new WaDbContext(_options);
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, Encoding.UTF8.GetBytes("test-key"));
        var sender = new CapturingEmailSender();
        var handler = new SendNotificationEmailsHandler(
            new EmailStore(workerDb),
            sender,
            new StubbleTemplateRenderer(NullLogger<StubbleTemplateRenderer>.Instance, telemetry),
            new InMemoryEventPublisher(),
            new EventDeduplicator(() => DateTimeOffset.UtcNow),
            telemetry,
            config,
            NullLogger<SendNotificationEmailsHandler>.Instance,
            new SenderBrandingStore(workerDb),
            new FeaturesProvider(
                new PlansCache(new PlansStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
                new FlagsCache(new FlagsStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow)), TimeProvider.System);

        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.TransferCreated,
            Version = 1,
            OccurredAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid(),
            PartitionKey = "abcdefgh",
            Payload = new
            {
                transferId,
                linkId = "abcdefgh",
                totalBytes = 0,
                fileCount = 0,
                emails = new[] { "recipient@example.com" },
                senderEmail = (string?)null,
                scheduledSendAt = (DateTime?)null
            }
        };

        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
        return Assert.Single(sender.Sent);
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }
}
