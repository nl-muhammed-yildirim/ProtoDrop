namespace wa.application.UseCases.Admin;

/// <summary>T-011-04: one row in the admin transfer search results (TA-4.2#21).</summary>
/// <param name="Id">Primary key of the transfer.</param>
/// <param name="LinkId">Public 8-character link id.</param>
/// <param name="OwnerEmail">Email of the owning user; null for anonymous transfers; PII (TA-9.4).</param>
/// <param name="Status">Stored transfer status byte (see <c>TransferStatus</c>).</param>
/// <param name="FileCount">Number of files in the transfer.</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="CreatedAtUtc">UTC instant the transfer was created.</param>
/// <param name="DownloadsCount">Downloads served so far.</param>
public record AdminTransferRow(
    Guid Id,
    string LinkId,
    string? OwnerEmail,
    byte Status,
    int FileCount,
    long TotalBytes,
    DateTime CreatedAtUtc,
    int DownloadsCount);
