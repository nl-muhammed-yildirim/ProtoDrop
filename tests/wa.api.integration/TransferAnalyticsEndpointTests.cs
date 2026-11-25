using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>feature.analytics flipped on before the first request.</summary>
public class TransferAnalyticsEndpointTests() : TransferAnalyticsEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task ProUser_WithDownloads_200_DailyTopCountryTopBrowserTotal() // AC-023-1 / story AC 1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var nowUtc = DateTime.UtcNow;   // one clock read: seeding and the date assertion must agree across UTC midnight
        var today = nowUtc.Date.AddHours(1);
        await AddEventsAsync(
            Event(id, today, "NL", "Chrome"), Event(id, today.AddDays(-1), "NL", "Chrome"), Event(id, today.AddDays(-2), "NL", "Firefox"),
            Event(id, today, "DE", "Chrome"),
            Event(id, today, null, null), Event(id, today.AddDays(-1), null, null));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(6, body.GetProperty("total").GetInt32());
        var countries = body.GetProperty("countries").EnumerateArray().Select(c => (c.GetProperty("code").GetString(), c.GetProperty("count").GetInt32())).ToList();
        Assert.Equal([("NL", 3), ("??", 2), ("DE", 1)], countries);
        Assert.Equal("Chrome", body.GetProperty("browsers")[0].GetProperty("family").GetString());
        var daily = body.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal(nowUtc.ToString("yyyy-MM-dd"), daily[^1].GetProperty("date").GetString());
        Assert.Equal(3, daily[^1].GetProperty("count").GetInt32());
        Assert.Equal(daily.Sum(d => d.GetProperty("count").GetInt32()), body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ProUser_Body_HasNoRawIpUaOrHashes() // AC-023-2
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), "NL", "Chrome"));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENTINEL", raw);
        Assert.DoesNotContain("Mozilla", raw);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(["total", "daily", "countries", "browsers", "files", "downloadAll", "topFiles"], doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task ProUser_ZeroDownloads_200_EmptyState() // EC-023-1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.All(body.GetProperty("daily").EnumerateArray(), d => Assert.Equal(0, d.GetProperty("count").GetInt32()));
        Assert.Equal(0, body.GetProperty("countries").GetArrayLength());
        Assert.Equal(0, body.GetProperty("browsers").GetArrayLength());
    }

    [Fact]
    public async Task ProUser_NullCountry_Unknown_NullFamily_Other() // EC-023-2/3
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), null, null));

        var body = await ReadJsonAsync(await AnalyticsAsync(id, userId));

        Assert.Equal("??", body.GetProperty("countries")[0].GetProperty("code").GetString());
        Assert.Equal("Other", body.GetProperty("browsers")[0].GetProperty("family").GetString());
    }

    [Theory]
    [InlineData(TransferStatus.Expired)]
    [InlineData(TransferStatus.DownloadLimit)]
    public async Task ProUser_ExpiredTransfer_200(byte status) // EC-023-5
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId, status);
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), "NL", "Chrome"));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadJsonAsync(response)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ProUser_SendDayEvent_Counted_WindowFloorIsSendDate() // FR-023-1: window starts at the transfer's own send date
    {
        var userId = await SeedUserAsync("pro");
        var created = DateTime.UtcNow.AddDays(-20);
        var id = await SeedTransferAsync(userId, createdUtc: created);
        await AddEventsAsync(Event(id, created.AddMinutes(5), "NL", "Chrome"), Event(id, DateTime.UtcNow.AddHours(-1), "DE", "Chrome"));

        var body = await ReadJsonAsync(await AnalyticsAsync(id, userId));

        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(created.ToString("yyyy-MM-dd"), body.GetProperty("daily")[0].GetProperty("date").GetString());
    }

    [Fact]
    public async Task FreeUser_403Forbidden() // AC-023-3 server half / EC-023-6
    {
        var userId = await SeedUserAsync("free");
        var id = await SeedTransferAsync(userId);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("FORBIDDEN", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Guest_401()
    {
        var id = await SeedTransferAsync(null);

        using var response = await AnalyticsAsync(id, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForeignOwner_404NotFound()
    {
        var owner = await SeedUserAsync("pro");
        var other = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(owner);

        using var response = await AnalyticsAsync(id, other);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("NOT_FOUND", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeletedTransfer_404NotFound()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId, deleted: true);

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProUser_BillingPlan_FeaturesAnalyticsTrue()
    {
        var userId = await SeedUserAsync("pro");

        var body = await ReadJsonAsync(await GetAsync("/api/v1/billing/plan", userId));

        Assert.True(body.GetProperty("features").GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public async Task FreeUser_BillingPlan_FeaturesAnalyticsFalse()
    {
        var userId = await SeedUserAsync("free");

        var body = await ReadJsonAsync(await GetAsync("/api/v1/billing/plan", userId));

        Assert.False(body.GetProperty("features").GetProperty("analytics").GetBoolean());
    }

    [Fact]
    public async Task TenThousandEvents_TotalCorrect() // EC-023-4
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var now = DateTime.UtcNow;
        var rows = Enumerable.Range(0, 10_000)
            .Select(i => Event(id, now.AddMinutes(-(i % 2000)), i % 2 == 0 ? "NL" : "DE", i % 3 == 0 ? "Chrome" : "Firefox"))
            .ToArray();
        Db.Set<DownloadEvent>().AddRange(rows);
        await Db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        using var response = await AnalyticsAsync(id, userId);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10_000, (await ReadJsonAsync(response)).GetProperty("total").GetInt32());
        Console.WriteLine($"analytics over 10k events: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task DailyQuery_SeeksIxDownEventT()
    {
        var id = await SeedTransferAsync(null);
        await AddEventsAsync(Enumerable.Range(0, 50).Select(i => Event(id, DateTime.UtcNow.AddHours(-i), "NL", "Chrome")).ToArray());

        await using var ctx = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(Sql.GetConnectionString()).Options);
        var sql = TransferAnalyticsStore.DailyQuery(ctx.Set<DownloadEvent>(), id, DateTime.UtcNow.AddDays(-30)).ToQueryString();

        await using var conn = new SqlConnection(Sql.GetConnectionString());
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
        Assert.Contains("Index=\"[IX_DownEvent_T]\"", xml);
        Assert.Contains("<SeekPredicates>", xml);
    }

    [Fact]
    public async Task ProUser_ThreeFiles_PerFileCounts_DownloadAllSeparate() // FR-023-2, story AC 1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var f = await SeedFilesAsync(id, "a.txt", "b.txt", "c.txt");
        var now = DateTime.UtcNow;
        await AddEventsAsync(
            Event(id, now.AddHours(-3), "NL", "Chrome", f[0]), Event(id, now.AddHours(-2), "NL", "Chrome", f[0]), Event(id, now.AddHours(-1), "NL", "Chrome", f[0]),
            Event(id, now.AddHours(-1), "NL", "Chrome", f[1]),
            Event(id, now.AddHours(-1), "NL", "Chrome"), Event(id, now.AddHours(-2), "NL", "Chrome"));

        using var response = await AnalyticsAsync(id, userId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var files = body.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(["a.txt", "b.txt", "c.txt"], files.Select(x => x.GetProperty("name").GetString()));
        Assert.Equal([3, 1, 0], files.Select(x => x.GetProperty("count").GetInt32()));
        Assert.Equal(2, body.GetProperty("downloadAll").GetInt32());
        Assert.Equal(["a.txt", "b.txt"], body.GetProperty("topFiles").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ProUser_TopFiles_TieBrokenByRecency() // AC-023-5
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var f = await SeedFilesAsync(id, "a.txt", "b.txt");
        var now = DateTime.UtcNow;
        await AddEventsAsync(
            Event(id, now.AddHours(-5), "NL", "Chrome", f[0]), Event(id, now.AddHours(-4), "NL", "Chrome", f[0]),
            Event(id, now.AddHours(-3), "NL", "Chrome", f[1]), Event(id, now.AddHours(-1), "NL", "Chrome", f[1]));

        using var response = await AnalyticsAsync(id, userId);

        var body = await ReadJsonAsync(response);
        Assert.Equal(["b.txt", "a.txt"], body.GetProperty("topFiles").EnumerateArray().Select(x => x.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task ProUser_SingleFile_DownloadAllNull()
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        var f = await SeedFilesAsync(id, "only.txt");
        await AddEventsAsync(Event(id, DateTime.UtcNow.AddHours(-1), "NL", "Chrome", f[0]));

        using var response = await AnalyticsAsync(id, userId);

        var body = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("downloadAll").ValueKind);
        Assert.Equal(1, body.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task ProUser_ZeroDownloads_FilesListedAtZero() // EC-023-1
    {
        var userId = await SeedUserAsync("pro");
        var id = await SeedTransferAsync(userId);
        await SeedFilesAsync(id, "a.txt", "b.txt");

        using var response = await AnalyticsAsync(id, userId);

        var body = await ReadJsonAsync(response);
        Assert.Equal([0, 0], body.GetProperty("files").EnumerateArray().Select(x => x.GetProperty("count").GetInt32()));
        Assert.Equal(0, body.GetProperty("downloadAll").GetInt32());
        Assert.Equal(0, body.GetProperty("topFiles").GetArrayLength());
    }

    [Fact]
    public async Task FileQuery_SeeksIxDownEventT()
    {
        var id = await SeedTransferAsync(null);
        // FileId is not in IX_DownEvent_T (key lookup). ToQueryString declares locals, so the optimizer uses average density:
        // spread events over many transfers so a seek + lookup is estimated cheaper than a clustered scan (needs ~100k rows).
        for (var t = 0; t < 200; t++)
        {
            var other = await SeedTransferAsync(null);
            await AddEventsAsync(Enumerable.Range(0, 500).Select(i => Event(other, DateTime.UtcNow.AddMinutes(-i), "NL", "Chrome", Guid.NewGuid())).ToArray());
        }
        await AddEventsAsync(Enumerable.Range(0, 50).Select(i => Event(id, DateTime.UtcNow.AddHours(-i), "NL", "Chrome", i % 2 == 0 ? Guid.NewGuid() : null)).ToArray());

        await using var ctx = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(Sql.GetConnectionString()).Options);
        var sql = TransferAnalyticsStore.FileQuery(ctx.Set<DownloadEvent>(), id, DateTime.UtcNow.AddDays(-30)).ToQueryString();

        await using var conn = new SqlConnection(Sql.GetConnectionString());
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
        Assert.Contains("Index=\"[IX_DownEvent_T]\"", xml);
        Assert.Contains("<SeekPredicates>", xml);
    }

    [Fact]
    public async Task RecordSingleFileDownload_PersistsUserAgentFamily()
    {
        var id = await SeedTransferAsync(null);

        await new PublicTransferStore(Db, TimeProvider.System).RecordSingleFileDownloadAsync(id, Guid.NewGuid(), null, null, "Firefox", CancellationToken.None);

        var family = await Db.Set<DownloadEvent>().AsNoTracking().Where(e => e.TransferId == id).Select(e => e.UserAgentFamily).SingleAsync();
        Assert.Equal("Firefox", family);
    }
}
