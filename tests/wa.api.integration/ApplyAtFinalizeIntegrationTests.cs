using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.ValueObjects;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// T-031-04 (US-018-04 AC block 2, EC-007-1, FR-018-4): a limit flag raised between two sends applies to the next send only.
/// Real SQL 2022 via Testcontainers (TA-14.3); SendTransferHandler wired directly (no web host, no 30 s TTL wait).
/// </summary>
public class ApplyAtFinalizeIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer _sql = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private WaDbContext _storeDb = null!;
    private WaDbContext _cacheDb = null!;
    private LimitsProvider _limits = null!;
    private FeaturesProvider _features = null!;
    private readonly RecordingPublisher _publisher = new();

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _storeDb = new WaDbContext(_options);
        await _storeDb.Database.MigrateAsync();
        _cacheDb = new WaDbContext(_options);

        var plansCache = new PlansCache(new PlansStore(_cacheDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow);
        var flagsCache = new FlagsCache(new FlagsStore(_cacheDb), TimeSpan.Zero, () => DateTimeOffset.UtcNow);
        _limits = new LimitsProvider(plansCache, flagsCache);
        _features = new FeaturesProvider(plansCache, flagsCache);
    }

    // US-020-02: one bound accessor per "request" (the real one is scoped + memoized) — a reused handler would pin the first resolution.
    private SendTransferHandler NewHandler()
    {
        var accessor = new PlanContextAccessor(new UserPlanStore(_cacheDb), _limits, _features);
        accessor.Bind(null); // guest -> Free, as before
        return new SendTransferHandler(
            new SendStore(_storeDb, new ThrowingBlobStore()),
            new TransferStore(_storeDb),
            accessor,
            _publisher,
            new WaTelemetryReporter(NullLogger<WaTelemetryReporter>.Instance, Encoding.UTF8.GetBytes("test-key")),
            NullLogger<SendTransferHandler>.Instance,
            new AppUserStore(_storeDb), TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        await _storeDb.DisposeAsync();
        await _cacheDb.DisposeAsync();
        await _sql.DisposeAsync();
    }

    [Fact]
    public async Task FlagRaised_InFlightTransferKeepsSendTimeLimits_NextSendUsesRaisedValues()
    {
        await using var seedDb = new WaDbContext(_options);
        var free = await seedDb.Set<Plan>().AsNoTracking().SingleAsync(p => p.Code == Plan.DefaultCode);
        var baseline = JsonSerializer.Deserialize<LimitsRecord>(free.LimitsJson)!;

        var a = await SeedDraftAsync();
        var sentAt = DateTime.UtcNow;
        await NewHandler().Handle(new SendTransferCommand(a, null, null, null, []), CancellationToken.None);

        var aAfterFirst = await ReloadAsync(a);
        Assert.Equal(baseline.MaxDownloads, aAfterFirst.MaxDownloads);
        Assert.InRange(aAfterFirst.ExpiresAtUtc, sentAt.AddDays(baseline.RetentionDays).AddMinutes(-1), sentAt.AddDays(baseline.RetentionDays).AddMinutes(1));

        var raisedRetention = baseline.RetentionDays + 7;
        var raisedCap = baseline.MaxDownloads + 150;
        await UpsertFlagAsync("limits." + Plan.DefaultCode + ".retentionDays", raisedRetention.ToString());
        await UpsertFlagAsync("limits." + Plan.DefaultCode + ".maxDownloads", raisedCap.ToString());

        var b = await SeedDraftAsync();
        var sentBAt = DateTime.UtcNow;
        await NewHandler().Handle(new SendTransferCommand(b, null, null, null, []), CancellationToken.None);

        var bAfter = await ReloadAsync(b);
        Assert.Equal(raisedCap, bAfter.MaxDownloads);
        Assert.InRange(bAfter.ExpiresAtUtc, sentBAt.AddDays(raisedRetention).AddMinutes(-1), sentBAt.AddDays(raisedRetention).AddMinutes(1));

        var aAfterSecond = await ReloadAsync(a);
        Assert.Equal(aAfterFirst.ExpiresAtUtc, aAfterSecond.ExpiresAtUtc);
        Assert.Equal(aAfterFirst.MaxDownloads, aAfterSecond.MaxDownloads);
        Assert.Equal(1, aAfterSecond.Status);
    }

    private async Task<Guid> SeedDraftAsync()
    {
        await using var db = new WaDbContext(_options);
        var id = Guid.NewGuid();
        db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = LinkId.Mint().Value,
            Status = 0,
            SenderName = "Guest",
            TotalBytes = 512,
            FileCount = 1,
            MaxDownloads = 1,
            ExpiresAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Transfer> ReloadAsync(Guid id)
    {
        await using var db = new WaDbContext(_options);
        return await db.Set<Transfer>().AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task UpsertFlagAsync(string key, string value)
    {
        await using var db = new WaDbContext(_options);
        var flag = await db.Set<FeatureFlag>().SingleOrDefaultAsync(f => f.Key == key);
        if (flag is null)
            db.Set<FeatureFlag>().Add(new FeatureFlag { Key = key, Value = value, UpdatedAtUtc = DateTime.UtcNow });
        else
        {
            flag.Value = value;
            flag.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    private sealed class ThrowingBlobStore : IBlobStore
    {
        public Task UploadAsync(string path, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
