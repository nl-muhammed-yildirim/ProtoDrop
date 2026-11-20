using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
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
/// F-PRF-002 US-022-04: f-expire schedule sub-step + f-email notify gate against a real SQL Server (Testcontainers).
/// The fake clock is the explicit nowUtc passed to RunAsync. feature.scheduling stays as seeded (off) — the timer ignores the gate by design.
/// </summary>
public class ScheduledSendTimerIntegrationTests : IAsyncLifetime
{
    // A fixed instant on a 15-min boundary.
    private static readonly DateTime T0 = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);

    private MsSqlContainer _sql = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private WaDbContext _db = null!;
    private SbEventPublisher _publisher = null!;
    private readonly CapturingEmailSender _sender = new();

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(_options);
        await _db.Database.MigrateAsync();

        var sbConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wa:ServiceBus:ConnectionString"] = "Endpoint=tcp://127.0.0.1:9;SharedAccessKeyName=Dummy;SharedAccessKey=dummy" // unreachable: the outbox row still commits (TA-4.1.7)
        }).Build();
        _publisher = new SbEventPublisher(sbConfig, NullLogger<SbEventPublisher>.Instance, new WaDbContextFactory(_options));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private ActivateScheduledTransfersHandler BuildTimer() =>
        new(new ScheduledSendStore(new WaDbContextFactory(_options)), _publisher, NullLogger<ActivateScheduledTransfersHandler>.Instance);

    /// <summary>f-email handler on a fresh context per call (EmailStore reads tracked rows; a long-lived context would return stale status).</summary>
    private async Task DeliverToEmailAsync(EventEnvelope envelope)
    {
        await using var workerDb = new WaDbContext(_options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Wa:Url:Public"] = "http://localhost:5173",
            ["Wa:Email:FromAddress"] = "no-reply@protodrop.com"
        }).Build();
        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, Encoding.UTF8.GetBytes("test-key"));
        var handler = new SendNotificationEmailsHandler(
            new EmailStore(workerDb),
            _sender,
            new StubbleTemplateRenderer(NullLogger<StubbleTemplateRenderer>.Instance, telemetry),
            new InMemoryEventPublisher(),
            new EventDeduplicator(() => DateTimeOffset.UtcNow),
            telemetry,
            config,
            NullLogger<SendNotificationEmailsHandler>.Instance,
            new SenderBrandingStore(workerDb),
            new FeaturesProvider(
                new PlansCache(new PlansStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow),
                new FlagsCache(new FlagsStore(workerDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow)));
        await handler.HandleAsync(envelope, 1, 5, CancellationToken.None);
    }

    private async Task<Guid> SeedAsync(string linkId, byte status, DateTime? due, DateTime expiresAt, string address = "r@example.com", int maxDownloads = 100, int downloads = 0)
    {
        var id = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = linkId,
            Status = status,
            ScheduledSendAtUtc = due,
            ExpiresAtUtc = expiresAt,
            MaxDownloads = maxDownloads,
            DownloadsCount = downloads,
            SenderName = "Anna K.",
            SenderEmail = "anna@example.com",
            TotalBytes = 1024,
            FileCount = 1,
            CreatedAtUtc = T0.AddDays(-1)
        });
        _db.Set<EmailRecipient>().Add(new EmailRecipient { Id = Guid.NewGuid(), TransferId = id, Address = address });
        await _db.SaveChangesAsync();
        return id;
    }

    private static EventEnvelope CreatedEnvelope(Guid id, string linkId, DateTime? due, string address = "r@example.com") => new()
    {
        EventId = Guid.NewGuid(),
        Type = EventType.TransferCreated,
        Version = 1,
        OccurredAtUtc = T0,
        CorrelationId = Guid.NewGuid(),
        PartitionKey = linkId,
        Payload = new
        {
            transferId = id,
            linkId,
            totalBytes = 1024,
            fileCount = 1,
            emails = new[] { address },
            senderEmail = "anna@example.com",
            scheduledSendAt = due
        }
    };

    private async Task<List<DomainEvent>> OutboxAsync(string type)
    {
        await using var fresh = new WaDbContext(_options);
        return await fresh.Set<DomainEvent>().AsNoTracking().Where(e => e.EventType == type).ToListAsync();
    }

    private async Task<byte> StatusAsync(Guid id)
    {
        await using var fresh = new WaDbContext(_options);
        return await fresh.Set<Transfer>().AsNoTracking().Where(t => t.Id == id).Select(t => t.Status).SingleAsync();
    }

    [Fact]
    public async Task AC0221_NoEmailBeforeDue_EmailWithinOnePeriodAfter()
    {
        var due = T0.AddHours(2);
        var id = await SeedAsync("SCHED01A", TransferStatus.Scheduled, due, T0.AddDays(7));
        var original = CreatedEnvelope(id, "SCHED01A", due);

        // 1. The original transfer.created (scheduledSendAt set) reaches f-email early: nothing is sent.
        await DeliverToEmailAsync(original);
        Assert.Empty(_sender.Sent);
        await using (var check = new WaDbContext(_options))
            Assert.Null((await check.Set<EmailRecipient>().AsNoTracking().SingleAsync(r => r.TransferId == id)).NotifiedAtUtc);

        // 2. Timer one hour in: not due yet.
        Assert.Equal(0, await BuildTimer().RunAsync(T0.AddHours(1), CancellationToken.None));
        Assert.Equal(TransferStatus.Scheduled, await StatusAsync(id));
        Assert.Empty(await OutboxAsync("transfer.created"));

        // 3. Timer 5 minutes after due, in a later 15-min window: flips and re-emits exactly once.
        Assert.Equal(1, await BuildTimer().RunAsync(due.AddMinutes(5), CancellationToken.None));
        Assert.Equal(TransferStatus.Active, await StatusAsync(id));
        var rows = await OutboxAsync("transfer.created");
        var row = Assert.Single(rows);
        var reEmitted = JsonSerializer.Deserialize<EventEnvelope>(row.EnvelopeJson)!;
        Assert.NotEqual(original.EventId, reEmitted.EventId);
        Assert.Equal("SCHED01A", reEmitted.PartitionKey);

        await DeliverToEmailAsync(reEmitted);
        Assert.Equal("r@example.com", Assert.Single(_sender.Sent).To);
        await using (var check = new WaDbContext(_options))
            Assert.NotNull((await check.Set<EmailRecipient>().AsNoTracking().SingleAsync(r => r.TransferId == id)).NotifiedAtUtc);

        // 4. Broker redelivery of the original envelope: still one email.
        await DeliverToEmailAsync(original);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task AC0225_ScheduledExpiresBeforeDue_ExpiredNoEmail()
    {
        var due = T0.AddHours(2);
        var id = await SeedAsync("SCHED02A", TransferStatus.Scheduled, due, T0.AddHours(-1));
        var original = CreatedEnvelope(id, "SCHED02A", due);

        var telemetry = new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, new byte[32]);
        var expired = await new ExpireTransfersHandler(new ExpiryStore(new WaDbContext(_options)), _publisher, telemetry,
            NullLogger<ExpireTransfersHandler>.Instance).RunAsync(T0, CancellationToken.None);
        Assert.Equal(1, expired);
        Assert.Equal(TransferStatus.Expired, await StatusAsync(id));
        Assert.Single(await OutboxAsync("transfer.expired"));

        Assert.Equal(0, await BuildTimer().RunAsync(due.AddMinutes(5), CancellationToken.None));
        Assert.Empty(await OutboxAsync("transfer.created"));

        await DeliverToEmailAsync(original);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task AC0226_DoubleRunSameWindow_OneFlip_OneEvent()
    {
        var id = await SeedAsync("SCHED03A", TransferStatus.Scheduled, T0.AddMinutes(-10), T0.AddDays(7));

        Assert.Equal(1, await BuildTimer().RunAsync(T0.AddMinutes(1), CancellationToken.None));
        Assert.Equal(0, await BuildTimer().RunAsync(T0.AddMinutes(1).AddSeconds(30), CancellationToken.None));
        Assert.Single(await OutboxAsync("transfer.created"));

        // Second guard alone: drop the claim row, same window, the conditional UPDATE still flips only once.
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM [JobRun] WHERE [JobKey] = 'schedule'");
        Assert.Equal(0, await BuildTimer().RunAsync(T0.AddMinutes(2), CancellationToken.None));
        Assert.Single(await OutboxAsync("transfer.created"));
        Assert.Equal(TransferStatus.Active, await StatusAsync(id));
    }

    [Fact]
    public async Task EC0221_MissedRuns_NextRunCatchesUp()
    {
        var id = await SeedAsync("SCHED04A", TransferStatus.Scheduled, T0, T0.AddDays(7));

        Assert.Equal(1, await BuildTimer().RunAsync(T0.AddHours(3), CancellationToken.None));

        Assert.Equal(TransferStatus.Active, await StatusAsync(id));
        Assert.Single(await OutboxAsync("transfer.created"));
    }

    [Fact]
    public async Task EC0223_DeletedScheduled_Skipped()
    {
        var id = await SeedAsync("SCHED05A", TransferStatus.Deleted, T0.AddMinutes(-30), T0.AddDays(7));

        Assert.Equal(0, await BuildTimer().RunAsync(T0, CancellationToken.None));

        Assert.Equal(TransferStatus.Deleted, await StatusAsync(id));
        Assert.Empty(await OutboxAsync("transfer.created"));
    }

    [Fact]
    public async Task CapReachedWhileScheduled_FlipsToDownloadLimit()
    {
        var id = await SeedAsync("SCHED06A", TransferStatus.Scheduled, T0.AddHours(1), T0.AddDays(7), maxDownloads: 1, downloads: 1);

        var result = await new PublicTransferStore(new WaDbContext(_options))
            .RecordSingleFileDownloadAsync(id, Guid.NewGuid(), null, null, null, CancellationToken.None);

        Assert.Equal(SingleFileDownloadOutcome.CapReached, result.Outcome);
        Assert.Equal(TransferStatus.DownloadLimit, await StatusAsync(id));
        Assert.Equal(0, await BuildTimer().RunAsync(T0.AddHours(2), CancellationToken.None));
        Assert.Empty(await OutboxAsync("transfer.created"));
    }

    [Fact]
    public async Task DueQuery_SeeksIxTransferSched()
    {
        for (var i = 0; i < 200; i++)
            _db.Set<Transfer>().Add(new Transfer
            {
                Id = Guid.NewGuid(),
                LinkId = $"ACT{i:D5}",
                Status = TransferStatus.Active,
                ExpiresAtUtc = T0.AddDays(7),
                MaxDownloads = 100,
                SenderName = "A",
                TotalBytes = 1,
                FileCount = 1,
                CreatedAtUtc = T0.AddDays(-1)
            });
        for (var i = 0; i < 3; i++)
            _db.Set<Transfer>().Add(new Transfer
            {
                Id = Guid.NewGuid(),
                LinkId = $"SCH{i:D5}",
                Status = TransferStatus.Scheduled,
                ScheduledSendAtUtc = T0.AddMinutes(-i - 1),
                ExpiresAtUtc = T0.AddDays(7),
                MaxDownloads = 100,
                SenderName = "A",
                TotalBytes = 1,
                FileCount = 1,
                CreatedAtUtc = T0.AddDays(-1)
            });
        await _db.SaveChangesAsync();

        await using var ctx = new WaDbContext(_options);
        var sql = ScheduledSendStore.DueQuery(ctx.Set<Transfer>(), T0).Take(500).ToQueryString();

        await using var conn = new SqlConnection(_sql.GetConnectionString());
        await conn.OpenAsync();
        await using (var on = conn.CreateCommand())
        {
            on.CommandText = "SET SHOWPLAN_XML ON";
            await on.ExecuteNonQueryAsync();
        }

        var plan = new StringBuilder();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                    plan.Append(reader.GetString(0));
            } while (await reader.NextResultAsync());
        }

        await using (var off = conn.CreateCommand())
        {
            off.CommandText = "SET SHOWPLAN_XML OFF";
            await off.ExecuteNonQueryAsync();
        }

        var xml = plan.ToString();
        Assert.Contains("Index=\"[IX_Transfer_Sched]\"", xml);
        Assert.Contains("<SeekPredicates>", xml);
        Assert.DoesNotContain("Lookup=\"1\"", xml);
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
