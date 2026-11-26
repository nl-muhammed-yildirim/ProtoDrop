using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// CHORE-dbcontext-lifetime regression: the API no longer shares one singleton WaDbContext, so N parallel DB-backed requests
/// all succeed and write exact counts. Before the change, overlapping requests on the shared context threw EF's
/// "second operation started on this context" InvalidOperationException (HTTP 500).
/// Each fact builds its own host so the plan and flag caches start cold and the concurrent cache misses hit the stores too.
/// Fresh Azurite + SQL 2022 per instance (TA-14.3) — same setup as PublicTransferEndpointTests.
/// </summary>
public class ConcurrentDbAccessTests : IAsyncLifetime
{
    private AzuriteContainer _azurite = null!;
    private MsSqlContainer _sql = null!;
    private string _connString = null!;
    private WaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", "test-jwt-secret");
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");

        _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _sql.StartAsync();
        _connString = _sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", _connString);

        _db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    [Fact]
    public async Task ParallelPublicTransferReads_AllReturn200()
    {
        const int parallelRequests = 32;
        var linkId = wa.domain.ValueObjects.LinkId.Mint().Value;
        var transferId = await SeedTransferAsync(linkId, maxDownloads: 5);
        await SeedFilesAsync(transferId, count: 2);

        await using var factory = new TestFactory();
        using var client = factory.CreateClient();

        var tasks = Enumerable.Range(0, parallelRequests)
            .Select(_ => client.GetAsync($"/api/v1/public/transfers/{linkId}"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        try
        {
            for (var i = 0; i < responses.Length; i++)
                Assert.True(responses[i].StatusCode == HttpStatusCode.OK, $"request #{i} returned {(int)responses[i].StatusCode}");
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task ParallelDownloadUrlMints_AllSucceed_CountIsExact()
    {
        const int parallelRequests = 20;
        var linkId = wa.domain.ValueObjects.LinkId.Mint().Value;
        var transferId = await SeedTransferAsync(linkId, maxDownloads: 100);
        var fileIds = await SeedFilesAsync(transferId, count: 1);

        await using var factory = new TestFactory();
        using var client = factory.CreateClient();

        var tasks = Enumerable.Range(0, parallelRequests)
            .Select(_ => client.GetAsync($"/api/v1/public/transfers/{linkId}/files/{fileIds[0]:n}/download-url"))
            .ToArray();
        var responses = await Task.WhenAll(tasks);
        try
        {
            for (var i = 0; i < responses.Length; i++)
                Assert.True(responses[i].StatusCode == HttpStatusCode.OK, $"mint #{i} returned {(int)responses[i].StatusCode}");
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }

        // A fresh context: the seeding context still holds the pre-request values.
        await using var freshDb = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(_connString).Options);
        var transfer = await freshDb.Set<Transfer>().AsNoTracking().FirstAsync(t => t.Id == transferId);
        Assert.Equal(parallelRequests, transfer.DownloadsCount);
        Assert.Equal(parallelRequests, await freshDb.Set<DownloadEvent>().CountAsync(e => e.TransferId == transferId));
    }

    [Fact]
    public void ApiHost_RegistersFactory_NotSharedContext()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient(); // forces host build

        Assert.Null(factory.Services.GetService<WaDbContext>());
        Assert.NotNull(factory.Services.GetService<IDbContextFactory<WaDbContext>>());
    }

    private async Task<Guid> SeedTransferAsync(string linkId, int maxDownloads)
    {
        var id = Guid.NewGuid();
        _db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = linkId,
            Status = 1,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = maxDownloads,
            DownloadsCount = 0,
            SenderName = "Alice",
            TotalBytes = 0,
            FileCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return id;
    }

    /// <summary>Seeds <paramref name="count"/> FileItem rows (one BlobRef each — UQ_FileItem_T_B) and returns their ids; the mint does not check that the blob exists.</summary>
    private async Task<List<Guid>> SeedFilesAsync(Guid transferId, int count)
    {
        var fileIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var fileId = Guid.NewGuid();
            fileIds.Add(fileId);
            var blobRef = new BlobRef
            {
                Id = Guid.NewGuid(),
                BlobPath = $"transfers/{transferId:N}/{fileId:N}",
                SizeBytes = 10L,
                RefCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Set<BlobRef>().Add(blobRef);
            _db.Set<FileItem>().Add(new FileItem
            {
                Id = fileId,
                TransferId = transferId,
                BlobRefId = blobRef.Id,
                OriginalName = $"file{i}.txt",
                SizeBytes = 10L,
                ContentType = "text/plain",
                SortOrder = i
            });
        }
        await _db.SaveChangesAsync();

        var transfer = await _db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = count;
        transfer.TotalBytes = 10L * count;
        await _db.SaveChangesAsync();
        return fileIds;
    }

    private sealed class TestFactory : WebApplicationFactory<Program> { }
}
