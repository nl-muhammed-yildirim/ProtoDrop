using System.IO.Compression;
using System.Text.Json;

namespace wa.application.UseCases.DataExport;

/// <summary>
/// US-047-01 (FR-047-1): pure builder of the export zip — profile.json + transfers.json, metadata only (never file bytes).
/// BCL only (System.IO.Compression, System.Text.Json); property names pinned via anonymous objects.
/// </summary>
public static class DataExportArchive
{
    /// <summary>The profile entry constant; value <c>profile.json</c>.</summary>
    public const string ProfileEntry = "profile.json";
    /// <summary>The transfers entry constant; value <c>transfers.json</c>.</summary>
    public const string TransfersEntry = "transfers.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Status byte → wire name (Transfer.Status: 0 draft … 4 deleted).</summary>
    /// <param name="status">The status.</param>
    /// <returns>The resulting text.</returns>
    public static string StatusName(byte status) => status switch
    {
        0 => "draft",
        1 => "active",
        2 => "expired",
        3 => "downloadLimit",
        4 => "deleted",
        _ => "unknown",
    };

    /// <summary>Builds the zip bytes for a snapshot. Zero transfers → transfers.json is <c>[]</c> (EC-047-1).</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The matching byte items.</returns>
    public static byte[] Build(ExportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var profile = new
        {
            email = snapshot.Profile.Email,
            displayName = snapshot.Profile.DisplayName,
            theme = snapshot.Profile.Theme,
            plan = snapshot.Profile.PlanCode,
            createdAtUtc = Utc(snapshot.Profile.CreatedAtUtc),
            organizationName = snapshot.Profile.OrgName, // F-PRF-001 (FR-021-1): null when unset; logo bytes are never exported
            emailNotifications = snapshot.Profile.EmailNotifications, // F-XCT-003 (FR-046-1): effective column value — NULL → true at the store projection
        };

        var transfers = snapshot.Transfers.Select(t => new
        {
            id = t.Id,
            createdAtUtc = Utc(t.CreatedAtUtc),
            status = StatusName(t.Status),
            expiresAtUtc = Utc(t.ExpiresAtUtc),
            passwordSet = t.PasswordSet,
            files = t.Files.Select(f => new { name = f.Name, sizeBytes = f.SizeBytes, sha256 = (string?)null }).ToList(),
            recipients = t.Recipients,
            downloadsCount = t.DownloadsCount,
        }).ToList();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, ProfileEntry, profile);
            Write(zip, TransfersEntry, transfers);
        }
        return ms.ToArray();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static void Write<T>(ZipArchive zip, string name, T value)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, value, Json);
    }
}
