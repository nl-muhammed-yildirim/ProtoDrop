namespace wa.domain;

/// <summary>US-047-01 (FR-047-1/2): one GDPR data-export request. Table dbo.DataExport (TA-3.7 migration AddDataExport).</summary>
public class DataExport
{
    /// <summary>Gets or sets the ID of the data export.</summary>
    public Guid Id { get; set; }
    /// <summary>The requesting user. No FK: soft-deleted users keep their rows (EC-047-3).</summary>
    public Guid AppUserId { get; set; }
    /// <summary>0=Processing, 1=Ready, 2=Failed — see <see cref="DataExportState"/>.</summary>
    public byte State { get; set; }
    /// <summary>Canonical blob path of the zip once ready (BlobPaths.DataExportZip); null until then.</summary>
    public string? BlobPath { get; set; }
    /// <summary>Gets or sets the created at UTC of the data export.</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Gets or sets the ready at UTC of the data export.</summary>
    public DateTime? ReadyAtUtc { get; set; }
}
