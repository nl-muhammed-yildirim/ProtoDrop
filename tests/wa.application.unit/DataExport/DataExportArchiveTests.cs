using System.IO.Compression;
using System.Text.Json;
using wa.application.UseCases.DataExport;

namespace wa.application.unit.DataExport;

/// <summary>US-047-01 (AC-047-1, EC-047-1): the export zip payload shape.</summary>
public class DataExportArchiveTests
{
    private static readonly DateTime Created = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static ExportProfile Profile() => new("a@x.com", "Ada", "system", "free", Created);

    private static Dictionary<string, JsonElement> Read(byte[] zipBytes)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var result = new Dictionary<string, JsonElement>();
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            result[e.FullName] = JsonDocument.Parse(s).RootElement.Clone();
        }
        return result;
    }

    [Fact]
    public void Build_ContainsExactlyProfileAndTransfersEntries()
    {
        var parts = Read(DataExportArchive.Build(new ExportSnapshot(Profile(), [])));
        Assert.Equal(["profile.json", "transfers.json"], parts.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void Profile_HasExactKeysAndValues()
    {
        var p = Read(DataExportArchive.Build(new ExportSnapshot(Profile(), [])))["profile.json"];
        Assert.Equal("a@x.com", p.GetProperty("email").GetString());
        Assert.Equal("Ada", p.GetProperty("displayName").GetString());
        Assert.Equal("system", p.GetProperty("theme").GetString());
        Assert.Equal("free", p.GetProperty("plan").GetString());
        Assert.Equal(Created, p.GetProperty("createdAtUtc").GetDateTime().ToUniversalTime());
        Assert.True(p.GetProperty("emailNotifications").GetBoolean());
        Assert.Equal(6, p.EnumerateObject().Count());
    }

    [Fact]
    public void FiveTransfers_AreAllListedWithFilesRecipientsAndNullSha()
    {
        var transfers = Enumerable.Range(0, 5).Select(i => new ExportTransfer(
            Guid.NewGuid(), Created.AddDays(i), (byte)(i % 5), Created.AddDays(i + 7), i == 1, i,
            [new ExportFile($"f{i}.pdf", 10 + i)], [$"r{i}@x.com"])).ToList();

        var arr = Read(DataExportArchive.Build(new ExportSnapshot(Profile(), transfers)))["transfers.json"];

        Assert.Equal(5, arr.GetArrayLength());
        var names = new[] { "draft", "active", "expired", "downloadLimit", "deleted" };
        for (var i = 0; i < 5; i++)
        {
            var t = arr[i];
            Assert.Equal(transfers[i].Id, t.GetProperty("id").GetGuid());
            Assert.Equal(names[i], t.GetProperty("status").GetString());
            Assert.Equal(i == 1, t.GetProperty("passwordSet").GetBoolean());
            Assert.Equal(i, t.GetProperty("downloadsCount").GetInt32());
            var file = t.GetProperty("files")[0];
            Assert.Equal($"f{i}.pdf", file.GetProperty("name").GetString());
            Assert.Equal(10 + i, file.GetProperty("sizeBytes").GetInt64());
            Assert.Equal(JsonValueKind.Null, file.GetProperty("sha256").ValueKind);
            Assert.Equal($"r{i}@x.com", t.GetProperty("recipients")[0].GetString());
        }
    }

    [Fact]
    public void ZeroTransfers_YieldsEmptyArray_ProfileStillPresent()
    {
        var parts = Read(DataExportArchive.Build(new ExportSnapshot(Profile(), [])));
        Assert.Equal(JsonValueKind.Array, parts["transfers.json"].ValueKind);
        Assert.Equal(0, parts["transfers.json"].GetArrayLength());
        Assert.Equal("a@x.com", parts["profile.json"].GetProperty("email").GetString());
    }
}
