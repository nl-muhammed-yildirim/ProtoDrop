using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.Azurite;
using Testcontainers.MsSql;
using wa.application.Blob;
using wa.application.Limits;
using wa.domain;
using wa.domain.Transfers;
using wa.infrastructure.Persistence;

namespace wa.api.integration;

/// <summary>
/// US-045-01 (F-XCT-002, T-066): GET /api/v1/transfers?q= — owner-scoped search by file name or recipient behind the feature.search gate,
/// plus the widened admin q on endpoint 21. AC-045-1/2/3, EC-045-1/3/6, FR-045-5/7. Same container recipe as TransferAnalyticsEndpointTests;
/// Wa__AdminEmails + seeded admin user per AdminFlagsEndpointTests (process-global env vars — the runner is single-threaded).
/// </summary>
public abstract class TransferSearchEndpointTestsBase(bool gateOn) : IAsyncLifetime
{
    private const string TestSecret = "test-jwt-secret";
    private const string AdminEmail = "search-admin@x.com";

    private AzuriteContainer _azurite = null!;
    protected MsSqlContainer Sql = null!;
    private WebApplicationFactory<Program> _factory = null!;
    protected WaDbContext Db = null!;
    protected HttpClient Client = null!;
    protected Guid AdminUserId = default;   // endpoint 21 (T-011-01) — seeded before the factory builds.

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").Build();
        await _azurite.StartAsync();
        var port = _azurite.GetMappedPublicPort(AzuriteBuilder.BlobPort);
        var accountUrl = new Uri($"http://127.0.0.1:{port}/{AzuriteBuilder.AccountName}");

        Environment.SetEnvironmentVariable("Wa__Blob__AccountUrl", accountUrl.ToString());
        Environment.SetEnvironmentVariable("Wa__Blob__AccountKey", AzuriteBuilder.AccountKey);
        Environment.SetEnvironmentVariable("Wa__Jwt__Secret", TestSecret);
        Environment.SetEnvironmentVariable("Wa__Url__Public", "http://localhost:5173");
        // Role derives per request from Wa:AdminEmails (T-011-01 gate) — must be set before the host builds.
        Environment.SetEnvironmentVariable("Wa__AdminEmails", AdminEmail);
        Environment.SetEnvironmentVariable("Wa__ServiceBus__ConnectionString", "Endpoint=" + accountUrl + ";SharedAccessKeyName=Dummy;SharedAccessKey=«redacted-secret»");

        Sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await Sql.StartAsync();
        var connString = Sql.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__WaDb", connString);

        Db = new WaDbContext(new DbContextOptionsBuilder<WaDbContext>().UseSqlServer(connString).Options);
        await Db.Database.MigrateAsync();

        if (gateOn)
        {
            var flag = await Db.Set<FeatureFlag>().SingleAsync(f => f.Key == FeatureGates.Search);
            flag.Value = "true";
            flag.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
        }

        // Admin user for endpoint 21 (AdminFlagsEndpointTests recipe) — seeded before the factory builds so RequireAdminAsync can resolve it.
        var planId = await Db.Set<Plan>().Where(p => p.Code == "free").Select(p => p.Id).FirstAsync();
        AdminUserId = Guid.NewGuid();
        Db.Set<AppUser>().Add(new AppUser
        {
            Id = AdminUserId,
            Email = AdminEmail.ToLowerInvariant(),
            DisplayName = "Search Test",
            PlanId = planId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();

        var svc = new BlobServiceClient(accountUrl, new StorageSharedKeyCredential(AzuriteBuilder.AccountName, AzuriteBuilder.AccountKey));
        await svc.GetBlobContainerClient(BlobPaths.StagingContainer).CreateIfNotExistsAsync();
        await svc.GetBlobContainerClient(BlobPaths.TransfersContainer).CreateIfNotExistsAsync();

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await Db.DisposeAsync();
        await Sql.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    // ---- Helpers (TransferAnalyticsEndpointTests recipe; SeedTransferAsync gains senderName) ----

    protected async Task<Guid> SeedUserAsync(string planCode = "pro")
    {
        var planId = await Db.Set<Plan>().Where(p => p.Code == planCode).Select(p => p.Id).FirstAsync();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = $"search-{Guid.NewGuid():N}@example.com",
            DisplayName = "Search Test",
            PlanId = planId,
            Theme = "system",
            CreatedAtUtc = DateTime.UtcNow
        };
        Db.Set<AppUser>().Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    protected async Task<Guid> SeedTransferAsync(Guid? ownerId, byte status = TransferStatus.Active, DateTime? createdUtc = null, bool deleted = false, string senderName = "Anna K.")
    {
        var id = Guid.NewGuid();
        Db.Set<Transfer>().Add(new Transfer
        {
            Id = id,
            LinkId = Guid.NewGuid().ToString("N")[..8],
            OwnerAppUserId = ownerId,
            Status = status,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            MaxDownloads = 1000,
            SenderName = senderName,
            TotalBytes = 1,
            FileCount = 1,
            CreatedAtUtc = createdUtc ?? DateTime.UtcNow.AddDays(-10),
            DeletedAtUtc = deleted ? DateTime.UtcNow : null
        });
        await Db.SaveChangesAsync();
        return id;
    }

    /// <summary>US-045-01: FileItem (+ BlobRef) per name, SortOrder = index — the search matches OriginalName.</summary>
    protected async Task SeedFilesAsync(Guid transferId, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var fileId = Guid.NewGuid();
            var blobRefId = Guid.NewGuid();   // explicit id — mirrors TransferAnalyticsEndpointTests.SeedFilesAsync.
            Db.Set<BlobRef>().Add(new BlobRef
            {
                Id = blobRefId,
                BlobPath = BlobPaths.TransferFile(transferId, fileId),
                SizeBytes = 1,
                RefCount = 1,
                CreatedAtUtc = DateTime.UtcNow
            });
            Db.Set<FileItem>().Add(new FileItem
            {
                Id = fileId,
                TransferId = transferId,
                BlobRefId = blobRefId,
                OriginalName = names[i],
                SizeBytes = 1,
                ContentType = "text/plain",
                SortOrder = i
            });
        }

        var transfer = await Db.Set<Transfer>().FirstAsync(t => t.Id == transferId);
        transfer.FileCount = names.Length;   // mirrors TransferAnalyticsEndpointTests.SeedFilesAsync.
        await Db.SaveChangesAsync();
    }

    protected async Task SeedRecipientAsync(Guid transferId, string address)
    {
        Db.Set<EmailRecipient>().Add(new EmailRecipient
        {
            Id = Guid.NewGuid(),
            TransferId = transferId,
            Address = address
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>TA-9.1 session cookie (same idiom as PlanUsageEndpointTests).</summary>
    private static string CookieFor(Guid userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
        var unsigned = $"{userId:N}|{exp}";
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        return "wa.session=" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{unsigned}|{sig}"));
    }

    protected async Task<HttpResponseMessage> GetAsync(string url, Guid? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (userId is not null) request.Headers.Add("Cookie", CookieFor(userId.Value));
        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> SearchAsync(Guid? userId, string q) =>
        GetAsync($"/api/v1/transfers?q={Uri.EscapeDataString(q)}", userId);

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        return doc.RootElement.Clone();
    }
}

/// <summary>feature.search flipped on before the first request (flags cache TTL).</summary>
public class TransferSearchEndpointTests() : TransferSearchEndpointTestsBase(gateOn: true)
{
    [Fact]
    public async Task FileName_Financial_MatchFirst_HolidayAbsent() // story AC 1 / AC-045-1
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "q4-financial-report.pdf");
        await SeedRecipientAsync(t1, "a@b.com");
        var t2 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t2, "holiday.mp4");
        await SeedRecipientAsync(t2, "c@d.com");

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        // TA-4.2a wire: ids are 'n'-form — compare with N-form.
        Assert.Equal(t1.ToString("N"), body.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(t2.ToString("N"), body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Recipient_ExactEmail_Matches() // story AC 2 / FR-045-2 (real column: EmailRecipient.Address)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "notes.txt");
        await SeedRecipientAsync(t1, "a@b.com");

        using var response = await SearchAsync(userId, "a@b.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Sender_Name_Matches() // FR-045-2 (real column: Transfer.SenderName)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId, senderName: "Zorro Ltd");

        using var response = await SearchAsync(userId, "zorro");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Operators_AreLiteral() // AC-045-3 / FR-045-6: '*', '%', '_', '"' are literal characters, not LIKE operators
    {
        var userId = await SeedUserAsync();
        var t3 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t3, "report *.pdf");
        var t4 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t4, "report-final.pdf");

        using (var r1 = await SearchAsync(userId, "report *"))   // only the file whose name literally contains '*'
        {
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            var b1 = await ReadJsonAsync(r1);
            Assert.Single(b1.GetProperty("items").EnumerateArray(), i => t3.ToString("N") == i.GetProperty("id").GetString());
        }

        var t5 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t5, "50%_off.png");
        var t6 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t6, "500 off.png");   // '50%' as a LIKE pattern would match this; literally it must not

        using (var r2 = await SearchAsync(userId, "50%_"))
        {
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            var b2 = await ReadJsonAsync(r2);
            Assert.Single(b2.GetProperty("items").EnumerateArray(), i => t5.ToString("N") == i.GetProperty("id").GetString());
        }

        using (var r3 = await SearchAsync(userId, "\""))   // a lone double quote: 200 with an empty list
        {
            Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
            var b3 = await ReadJsonAsync(r3);
            Assert.Empty(b3.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public async Task FileAndRecipientMatch_SingleRow() // EC-045-6: one row per transfer — de-dup by construction (rank is a CASE over EXISTS)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "acme-1.pdf", "acme-2.pdf");
        await SeedRecipientAsync(t1, "acme@x.com");

        using var response = await SearchAsync(userId, "acme");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Single(body.GetProperty("items").EnumerateArray(), i => t1.ToString("N") == i.GetProperty("id").GetString());
    }

    [Fact]
    public async Task FileMatch_OutranksRecipientMatch() // FR-045-3: rank file=3 > recipient=2 — the earlier-created file match still comes first
    {
        var userId = await SeedUserAsync();
        var t7 = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow.AddDays(-10));
        await SeedFilesAsync(t7, "zeta.pdf");
        var t8 = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow);
        await SeedFilesAsync(t8, "a.pdf");
        await SeedRecipientAsync(t8, "zeta@y.com");

        using var response = await SearchAsync(userId, "zeta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal([t7.ToString("N"), t8.ToString("N")], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task NeverCrossesOwners() // FR-045-7: owner scope in the predicate — search never crosses owners
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "a-financial.pdf");
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "secret-financial.pdf");

        using (var rA = await SearchAsync(userA, "financial"))
        {
            Assert.Equal(HttpStatusCode.OK, rA.StatusCode);
            var bA = await ReadJsonAsync(rA);
            Assert.DoesNotContain(tB.ToString("N"), bA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
            Assert.Contains(tA.ToString("N"), bA.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        }

        using (var rB = await SearchAsync(userB, "financial"))
        {
            var bB = await ReadJsonAsync(rB);
            Assert.Single(bB.GetProperty("items").EnumerateArray(), i => tB.ToString("N") == i.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task DeletedTransfer_NotFound() // FR-045-7: not-deleted predicate — deleted rows never surface in search
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId, deleted: true);
        await SeedFilesAsync(t1, "gone-financial.pdf");

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task NoMatch_EmptyList() // EC-045-1: 200 with items [] and nextCursor null (not an error)
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);
        await SeedFilesAsync(t1, "notes.txt");

        using var response = await SearchAsync(userId, "zzz-no-match-zzz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.Null(body.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task QTooLong_400() // EC-045-3: 101 chars after trim → 400 Q_TOO_LONG; exactly 100 is fine
    {
        var userId = await SeedUserAsync();

        using (var longResponse = await SearchAsync(userId, new string('a', SearchTerm.MaxLength + 1)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);
            Assert.Equal("Q_TOO_LONG", (await ReadJsonAsync(longResponse)).GetProperty("code").GetString());
        }

        using var okResponse = await SearchAsync(userId, new string('a', SearchTerm.MaxLength));
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    [Fact]
    public async Task Paging_SearchCursor() // FR-045-3: the search cursor is bound to its term — 26 rows over two pages of 25+1
    {
        var userId = await SeedUserAsync();
        for (var i = 0; i < 26; i++)
            await SeedFilesAsync(await SeedTransferAsync(userId), $"deck-{i}.pdf");

        using var first = await SearchAsync(userId, "deck");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var body1 = await ReadJsonAsync(first);
        var page1Ids = body1.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(25, page1Ids.Count);
        var nextCursor = body1.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrEmpty(nextCursor));

        using (var second = await GetAsync($"/api/v1/transfers?q=deck&cursor={Uri.EscapeDataString(nextCursor!)}", userId))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var body2 = await ReadJsonAsync(second);
            var page2Ids = body2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
            Assert.Single(page2Ids);   // 26 total: the last row only — no id repeats across pages.
            Assert.DoesNotContain(page2Ids[0], page1Ids);
        }

        using (var foreign = await GetAsync($"/api/v1/transfers?q=other&cursor={Uri.EscapeDataString(nextCursor!)}", userId))
        {
            // The cursor's term differs from the current q → 400 VALIDATION (existing closed-list member).
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            var body = await ReadJsonAsync(foreign);
            Assert.Equal("VALIDATION", body.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Admin_FileName_CrossesOwners_ShowsOwner() // AC-045-2: endpoint 21 widens to file name / sender with the gate on; ownerEmail stays populated
    {
        var userA = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "a-financial.pdf");
        var userB = await SeedUserAsync();
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "b-financial.pdf");

        using var response = await GetAsync("/api/v1/admin/transfers?q=financial", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains(tA.ToString(), ids);   // endpoint 21 serializes Guids in the standard dashed form (TA-4.2a is n-form only on rows).
        Assert.Contains(tB.ToString(), ids);
        Assert.All(body.GetProperty("items").EnumerateArray(), i => Assert.False(string.IsNullOrEmpty(i.GetProperty("ownerEmail").GetString())));
    }

    [Fact]
    public async Task Admin_Recipient_CrossesOwners_ShowsOwner() // AC-045-2: admin searches the exact recipient "a@b.com" across owners — every transfer sent to that address is listed, owner email populated (EmailRecipient.Address match)
    {
        var userA = await SeedUserAsync();
        var tA = await SeedTransferAsync(userA);
        await SeedFilesAsync(tA, "notes-a.txt");
        await SeedRecipientAsync(tA, "a@b.com");

        var userB = await SeedUserAsync();
        var tB = await SeedTransferAsync(userB);
        await SeedFilesAsync(tB, "notes-b.txt");
        await SeedRecipientAsync(tB, "a@b.com");   // same recipient address, different owner — must also surface.

        using var response = await GetAsync("/api/v1/admin/transfers?q=a%40b.com", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var ids = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Contains(tA.ToString(), ids);   // dashed Guid form on endpoint 21 (TA-4.2a n-form is rows-only).
        Assert.Contains(tB.ToString(), ids);
        Assert.All(body.GetProperty("items").EnumerateArray(), i => Assert.False(string.IsNullOrEmpty(i.GetProperty("ownerEmail").GetString())));
    }

    [Fact]
    public async Task Admin_QTooLong_400() // EC-045-3 on endpoint 21: flag-on q over SearchTerm.MaxLength (100 chars) → 400 Q_TOO_LONG; exactly 100 is fine. Mirrors the gate-off FlagOff_LongQ_Ignored_200 contrast — here the gate is ON so the length check applies.
    {
        using (var longResponse = await GetAsync($"/api/v1/admin/transfers?q={new string('a', SearchTerm.MaxLength + 1)}", AdminUserId))
        {
            Assert.Equal(HttpStatusCode.BadRequest, longResponse.StatusCode);
            Assert.Equal("Q_TOO_LONG", (await ReadJsonAsync(longResponse)).GetProperty("code").GetString());
        }

        using var okResponse = await GetAsync($"/api/v1/admin/transfers?q={new string('a', SearchTerm.MaxLength)}", AdminUserId);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }
}

/// <summary>feature.search left at its seeded "false": q is ignored entirely on endpoint 14, and FR-011-3 stays exactly as it was.</summary>
public class TransferSearchEndpointGateOffTests() : TransferSearchEndpointTestsBase(gateOn: false)
{
    [Fact]
    public async Task FlagOff_QIgnored_PlainChronologicalList() // story AC 3, API half — q=financial returns the plain list, newest first
    {
        var userId = await SeedUserAsync();
        var tOld = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow.AddDays(-10));   // matches "financial" by file name…
        await SeedFilesAsync(tOld, "old-financial.pdf");
        var tNew = await SeedTransferAsync(userId, createdUtc: DateTime.UtcNow);               // …but is older; the plain list ignores q.

        using var response = await SearchAsync(userId, "financial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal([tNew.ToString("N"), tOld.ToString("N")], body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task FlagOff_LongQ_Ignored_200() // FR-045-5: q is ignored entirely — no length check either, because the gate is off
    {
        var userId = await SeedUserAsync();

        using var response = await SearchAsync(userId, new string('a', SearchTerm.MaxLength + 1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FlagOff_Admin_LegacyQ_NoFileNameMatch() // D7: FR-011-3 unchanged — LinkId exact OR recipient substring only, no file/sender widening
    {
        var userId = await SeedUserAsync();
        var t1 = await SeedTransferAsync(userId);   // file name contains "financial", but the legacy match never looks at it.
        await SeedFilesAsync(t1, "x-financial.pdf");

        using var response = await GetAsync("/api/v1/admin/transfers?q=financial", AdminUserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }
}
