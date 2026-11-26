using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-047-03 (FR-047-6, EC-047-3): the erasure report store against real SQL.
/// Deletions go through AppUserStore.SoftDeleteWithReHomeAsync; reads through ErasureReportStore.
/// </summary>
public class ErasureReportIntegrationTests : IAsyncLifetime
{
    private MsSqlContainer _sql = null!;
    private WaDbContext _db = null!;
    private DbContextOptions<WaDbContext> _options = null!;
    private Guid _planId;

    public async Task InitializeAsync()
    {
        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
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

    private ErasureReportStore Store() => new(new WaDbContextFactory(_options));

    private async Task<Guid> AddUserAsync(string email)
    {
        var id = Guid.NewGuid();
        _db.Set<AppUser>().Add(new AppUser { Id = id, Email = email, DisplayName = "U", PlanId = _planId, Theme = "system", CreatedAtUtc = DateTime.UtcNow.AddDays(-30) });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task AddTransferAsync(Guid owner, string link)
    {
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = Guid.NewGuid(),
            LinkId = link,
            OwnerAppUserId = owner,
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(3),
            MaxDownloads = 10,
            SenderName = "S",
            TotalBytes = 100,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();
    }

    private async Task DeleteAsync(Guid userId, DateTime? at = null, bool expectFirst = true)
    {
        var ok = await new AppUserStore(new AppDbContextFactory(_options)).SoftDeleteWithReHomeAsync(userId, at ?? DateTime.UtcNow, CancellationToken.None);
        if (expectFirst) Assert.True(ok);
    }

    [Fact]
    public async Task TwoDeletedAccounts_BothRows_WithEmailDeleteTimeAndReHomedCount()
    {
        var a = await AddUserAsync("dana@x.com");
        var b = await AddUserAsync("bob@y.org");
        await AddTransferAsync(a, "ACTIVE01");
        await AddTransferAsync(a, "ACTIVE02");
        await DeleteAsync(a, DateTime.UtcNow.AddMinutes(-2));
        await DeleteAsync(b, DateTime.UtcNow.AddMinutes(-1));

        var (items, next) = await Store().GetPageAsync(null, null, 25, CancellationToken.None);

        Assert.Null(next);
        Assert.Equal(2, items.Count);
        var rowA = items.Single(i => i.AppUserId == a);
        Assert.Equal("dana@x.com", rowA.Email);
        Assert.Equal(2, rowA.ReHomedTransferCount);
        Assert.True(rowA.DeletedAtUtc > DateTime.UtcNow.AddMinutes(-10));
        Assert.Null(rowA.SweptAtUtc);
        Assert.Equal("bob@y.org", items.Single(i => i.AppUserId == b).Email);
    }

    [Fact]
    public async Task AfterSweepMark_RowShowsSweptAt()
    {
        var a = await AddUserAsync("dana@x.com");
        await DeleteAsync(a);
        var sweptAt = DateTime.UtcNow;

        await new GdprSweepStore(new WaDbContextFactory(_options), TimeProvider.System).MarkSweptAsync(a, sweptAt, CancellationToken.None);

        var (items, _) = await Store().GetPageAsync(null, null, 25, CancellationToken.None);
        var row = Assert.Single(items);
        Assert.NotNull(row.SweptAtUtc);
        Assert.True(Math.Abs((row.SweptAtUtc!.Value - sweptAt).TotalSeconds) < 2);
    }

    [Fact]
    public async Task ZeroReHomed_RowShowsZero()
    {
        var a = await AddUserAsync("dana@x.com");
        await DeleteAsync(a);

        var (items, _) = await Store().GetPageAsync(null, null, 25, CancellationToken.None);

        Assert.Equal(0, Assert.Single(items).ReHomedTransferCount);
    }

    [Fact]
    public async Task DoubleDelete_ListsAccountOnce() // EC-047-3
    {
        var a = await AddUserAsync("dana@x.com");
        await DeleteAsync(a);
        await DeleteAsync(a, expectFirst: false);

        var (items, _) = await Store().GetPageAsync(null, null, 25, CancellationToken.None);

        Assert.Single(items);
    }

    [Fact]
    public async Task ThreeRows_LimitTwo_TwoPages_NoOverlap_NewestFirst()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = await AddUserAsync($"u{i}@x.com");
            ids.Add(id);
            await DeleteAsync(id, DateTime.UtcNow.AddMinutes(-30 + i)); // u2 is newest
        }

        var (page1, next) = await Store().GetPageAsync(null, null, 2, CancellationToken.None);
        Assert.Equal(2, page1.Count);
        Assert.NotNull(next);
        Assert.Equal([ids[2], ids[1]], page1.Select(r => r.AppUserId));

        var (afterUtc, afterId) = wa.domain.Transfers.CursorCodec.Decode(next!);
        var (page2, next2) = await Store().GetPageAsync(afterUtc, afterId, 2, CancellationToken.None);

        Assert.Null(next2);
        Assert.Equal([ids[0]], page2.Select(r => r.AppUserId));
    }

    [Fact]
    public async Task HardDeletedUser_EmailNull_RowStillListed()
    {
        var a = await AddUserAsync("dana@x.com");
        await DeleteAsync(a);
        await _db.Set<AppUser>().Where(u => u.Id == a).ExecuteDeleteAsync();

        var (items, _) = await Store().GetPageAsync(null, null, 25, CancellationToken.None);

        var row = Assert.Single(items);
        Assert.Equal(a, row.AppUserId);
        Assert.Null(row.Email);
    }
}
