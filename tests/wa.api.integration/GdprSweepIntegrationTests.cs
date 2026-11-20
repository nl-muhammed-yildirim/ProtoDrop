using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.UseCases.Gdpr;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-047-02 (AC-047-2, EC-047-3/4/5): the Erasure marker write and the f-gdpr-sweep handler against real SQL.
/// Fresh SQL 2022 per fact; the telemetry eraser is a recording stub.
/// </summary>
public class GdprSweepIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 19, 2, 0, 5, DateTimeKind.Utc);

    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private Guid _planId;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _options = new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_sql.GetConnectionString()).Options;
        _db = new WaDbContext(_options);
        await _db.Database.MigrateAsync();
        _planId = await _db.Set<Plan>().Where(p => p.Code == Plan.DefaultCode).Select(p => p.Id).FirstAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
    }

    private sealed class RecordingEraser : ITelemetryEraser
    {
        public List<Guid> Calls { get; } = [];
        public bool Down { get; set; }

        public Task EraseUserAsync(Guid userId, DateTime sinceUtc, CancellationToken ct)
        {
            Calls.Add(userId);
            return Down ? Task.FromException(new TelemetryUnavailableException("down")) : Task.CompletedTask;
        }
    }

    private async Task<Guid> AddUserAsync(string email)
    {
        var id = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser { Id = id, Email = email, DisplayName = "U", PlanId = _planId, Theme = "system", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task AddTransferAsync(Guid owner, string link, byte status)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = link,
            OwnerAppUserId = owner,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            MaxDownloads = 10,
            SenderName = "S",
            TotalBytes = 100,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();
    }

    private async Task DeleteAsync(Guid userId)
    {
        await using var ctx = new WaDbContext(_options);
        Assert.True(await new AppUserStore(ctx).SoftDeleteWithReHomeAsync(userId, DateTime.UtcNow, CancellationToken.None));
    }

    private async Task<Erasure> MarkerAsync(Guid userId)
    {
        await using var fresh = new WaDbContext(_options);
        return await fresh.Set<Erasure>().AsNoTracking().FirstAsync(e => e.AppUserId == userId);
    }

    private async Task BackdateAsync(Guid userId, TimeSpan age)
    {
        await using var fresh = new WaDbContext(_options);
        var at = DateTime.UtcNow - age;
        await fresh.Set<Erasure>().Where(e => e.AppUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAtUtc, at));
    }

    private static SweepErasedTelemetryHandler Handler(IGdprSweepStore store, ITelemetryEraser eraser) =>
        new(store, eraser, Microsoft.Extensions.Logging.Abstractions.NullLogger<SweepErasedTelemetryHandler>.Instance);

    private GdprSweepStore Store() => new(new WaDbContextFactory(_options));

    [Fact]
    public async Task SoftDelete_WritesPendingErasureRow_WithReHomedCount()
    {
        var id = await AddUserAsync("a@x.com");
        await AddTransferAsync(id, "ACTIVE01", 1);
        await AddTransferAsync(id, "ACTIVE02", 1);
        await AddTransferAsync(id, "EXPIRED1", 2);

        await DeleteAsync(id);

        var row = await MarkerAsync(id);
        Assert.Equal(2, row.ReHomedTransferCount);
        Assert.True(row.DeletedAtUtc > DateTime.UtcNow.AddMinutes(-5));
        Assert.Null(row.SweptAtUtc);
    }

    [Fact]
    public async Task SoftDeleteTwice_KeepsOneRow_FirstDeleteTime() // EC-047-3
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        var first = await MarkerAsync(id);

        await DeleteAsync(id);

        await using var fresh = new WaDbContext(_options);
        var rows = await fresh.Set<Erasure>().AsNoTracking().Where(e => e.AppUserId == id).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(first.DeletedAtUtc, row.DeletedAtUtc);
    }

    [Fact]
    public async Task Sweep_MarkerOlderThan24h_IsSweptWithRunTime() // AC-047-2
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        await BackdateAsync(id, TimeSpan.FromHours(25));
        var eraser = new RecordingEraser();

        var swept = await Handler(Store(), eraser).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, swept);
        Assert.Equal([id], eraser.Calls);
        Assert.NotNull((await MarkerAsync(id)).SweptAtUtc);
    }

    [Fact]
    public async Task Sweep_MarkerYoungerThan24h_StaysPending()
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        await BackdateAsync(id, TimeSpan.FromHours(23));
        var eraser = new RecordingEraser();

        var swept = await Handler(Store(), eraser).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, swept);
        Assert.Empty(eraser.Calls);
        Assert.Null((await MarkerAsync(id)).SweptAtUtc);
    }

    [Fact]
    public async Task Sweep_SecondNight_MarksNothingNew_KeepsSweptTime() // AC-047-2 block 2
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        await BackdateAsync(id, TimeSpan.FromHours(25));
        var eraser = new RecordingEraser();
        var handler = Handler(Store(), eraser);
        var night1 = DateTime.UtcNow;

        Assert.Equal(1, await handler.RunAsync(night1, CancellationToken.None));
        var sweptAt = (await MarkerAsync(id)).SweptAtUtc;

        Assert.Equal(0, await handler.RunAsync(night1.AddDays(1), CancellationToken.None));

        Assert.Single(eraser.Calls);
        Assert.Equal(sweptAt, (await MarkerAsync(id)).SweptAtUtc);
    }

    [Fact]
    public async Task Sweep_TelemetryUnavailable_StaysPending_NextNightSweeps() // EC-047-4
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        await BackdateAsync(id, TimeSpan.FromHours(25));
        var eraser = new RecordingEraser { Down = true };
        var handler = Handler(Store(), eraser);
        var night1 = DateTime.UtcNow;

        Assert.Equal(0, await handler.RunAsync(night1, CancellationToken.None));
        Assert.Null((await MarkerAsync(id)).SweptAtUtc);

        eraser.Down = false;
        Assert.Equal(1, await handler.RunAsync(night1.AddDays(1), CancellationToken.None));
        Assert.NotNull((await MarkerAsync(id)).SweptAtUtc);
    }

    [Fact]
    public async Task Sweep_ReRegisteredUser_KeysOnOldUserIdOnly() // EC-047-5
    {
        var oldId = await AddUserAsync("same@x.com");
        await DeleteAsync(oldId);
        await BackdateAsync(oldId, TimeSpan.FromHours(25));
        // UQ_AppUser_Email is unconditional, so the soft-deleted row's email is moved aside before the same address registers again.
        await _db.Set<AppUser>().Where(u => u.Id == oldId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Email, "gone@x.com"));
        var newId = await AddUserAsync("same@x.com");
        var eraser = new RecordingEraser();

        await Handler(Store(), eraser).RunAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal([oldId], eraser.Calls);
        await using var fresh = new WaDbContext(_options);
        Assert.False(await fresh.Set<Erasure>().AnyAsync(e => e.AppUserId == newId));
    }

    [Fact]
    public async Task Sweep_SameHourWindow_SecondRunClaimLost()
    {
        var id = await AddUserAsync("a@x.com");
        await DeleteAsync(id);
        await BackdateAsync(id, TimeSpan.FromHours(25));
        var store = Store();
        var window = SweepErasedTelemetryHandler.TruncateToHour(Now);

        Assert.True(await store.TryClaimAsync(window, Now.AddMinutes(30), CancellationToken.None));
        var eraser = new RecordingEraser();

        var swept = await Handler(store, eraser).RunAsync(Now, CancellationToken.None);

        Assert.Equal(0, swept);
        Assert.Empty(eraser.Calls);
    }
}
