namespace wa.application.UseCases.Admin;

/// <summary>T-011-04: one row in the admin transfer search results (TA-4.2#21).</summary>
public record AdminTransferRow(
    Guid Id,
    string LinkId,
    string? OwnerEmail,
    byte Status,
    int FileCount,
    long TotalBytes,
    DateTime CreatedAtUtc,
    int DownloadsCount);
