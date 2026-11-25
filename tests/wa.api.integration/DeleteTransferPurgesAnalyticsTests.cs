using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// F-PRF-003 fix round (FR-023-6): owner delete (TransferStore.MarkDeletedAsync) and admin delete
/// (AdminTransferStore.AdminMarkDeletedAsync) hard-delete the transfer's DownloadEvent rows in the same transaction as the
/// Status=4 flip; other transfers' rows are untouched. Fresh SQL 2022 via Testcontainers (TA-14.3), stores called directly.
/// </summary>
public class DeleteTransferPurgesAnalyticsTests : IAsyncLifetime
{
    private MsSqlContainer _container = null!;
    private string _connString = null!;
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _connString = _container.GetConnectionString();
        _db = NewDb();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private WaDbContext NewDb() => new(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);

    private async Task<Guid> SeedOwnerAsync()
    {
        var planId = await _db.Set<Plan>().Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"purge-{Guid.NewGuid():N}@example.com",
            DisplayName = "Purge Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Set<AppUser>().Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedTransferWithEventsAsync(Guid ownerId, int events)
    {
        var transferId = Guid.NewGuid();
        var blobRefId = Guid.NewGuid();
        _db.Set<BlobRef>().Add(new BlobRef
        {
            Id = blobRefId,
            BlobPath = $"transfers/{transferId:N}/blob.dat",
            SizeBytes = 1,
            RefCount = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        _db.Set<FileItem>().Add(new FileItem
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            BlobRefId = blobRefId,
            OriginalName = "a.txt",
            SizeBytes = 1,
            SortOrder = 0
        });
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = transferId,
            LinkId = Guid.NewGuid().ToString("N")[..8],
            OwnerAppUserId = ownerId,
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 100,
            SenderName = "Anna K.",
            TotalBytes = 1,
            FileCount = 1,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        });
        for (var i = 0; i < events; i++)
        {
            _db.Set<DownloadEvent>().Add(new DownloadEvent
            {
                TransferId = transferId,
                IpHash = new string('a', 64),
                UaHash = new string('b', 64),
                UserAgentFamily = "Chrome",
                CreatedAtUtc = DateTime.UtcNow.AddHours(-i - 1)
            });
        }
        await _db.SaveChangesAsync();
        return transferId;
    }

    private async Task<int> EventCountAsync(Guid transferId)
    {
        await using var fresh = NewDb();
        return await fresh.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId);
    }

    private async Task<byte> StatusAsync(Guid transferId)
    {
        await using var fresh = NewDb();
        return await fresh.Set<Transfer>().Where(t => t.Id == transferId).Select(t => t.Status).FirstAsync();
    }

    [Fact]
    public async Task OwnerDelete_RemovesThatTransfersDownloadEvents_LeavesOthers()
    {
        var owner = await SeedOwnerAsync();
        var target = await SeedTransferWithEventsAsync(owner, events: 3);
        var other = await SeedTransferWithEventsAsync(owner, events: 2);
        Assert.Equal(3, await EventCountAsync(target));

        var result = await new TransferStore(_db).MarkDeletedAsync(target, owner, DateTime.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal((byte)4, await StatusAsync(target));
        Assert.Equal(0, await EventCountAsync(target));
        Assert.Equal(2, await EventCountAsync(other));
        Assert.Equal((byte)1, await StatusAsync(other));
    }

    [Fact]
    public async Task OwnerDelete_ForeignOwner_NotFound_KeepsDownloadEvents()
    {
        var owner = await SeedOwnerAsync();
        var stranger = await SeedOwnerAsync();
        var target = await SeedTransferWithEventsAsync(owner, events: 2);

        var result = await new TransferStore(_db).MarkDeletedAsync(target, stranger, DateTime.UtcNow, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(2, await EventCountAsync(target));
        Assert.Equal((byte)1, await StatusAsync(target));
    }

    [Fact]
    public async Task AdminDelete_RemovesThatTransfersDownloadEvents_LeavesOthers()
    {
        var owner = await SeedOwnerAsync();
        var target = await SeedTransferWithEventsAsync(owner, events: 3);
        var other = await SeedTransferWithEventsAsync(owner, events: 2);
        Assert.Equal(3, await EventCountAsync(target));

        var result = await new AdminTransferStore(_db).AdminMarkDeletedAsync(target, DateTime.UtcNow, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal((byte)4, await StatusAsync(target));
        Assert.Equal(0, await EventCountAsync(target));
        Assert.Equal(2, await EventCountAsync(other));
        Assert.Equal((byte)1, await StatusAsync(other));
    }
}
