namespace wa.application.UseCases.Admin;

/// <summary>T-011-04: one row in the admin transfer search results (TA-4.2#21).</summary>
/// <param name="Id">Identifier of the admin transfer row.</param>
/// <param name="LinkId">Identifier of the link of the admin transfer row.</param>
/// <param name="OwnerEmail">The owner email of the admin transfer row.</param>
/// <param name="Status">The status of the admin transfer row.</param>
/// <param name="FileCount">The file count of the admin transfer row.</param>
/// <param name="TotalBytes">The total bytes of the admin transfer row.</param>
/// <param name="CreatedAtUtc">The created at UTC of the admin transfer row.</param>
/// <param name="DownloadsCount">The downloads count of the admin transfer row.</param>
public record AdminTransferRow(
    Guid Id,
    string LinkId,
    string? OwnerEmail,
    byte Status,
    int FileCount,
    long TotalBytes,
    DateTime CreatedAtUtc,
    int DownloadsCount);
