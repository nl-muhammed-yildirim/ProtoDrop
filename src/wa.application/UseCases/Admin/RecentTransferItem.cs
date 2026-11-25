namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: one row in the recent-transfers list (last 20 by CreatedAtUtc desc).</summary>
/// <param name="LinkId">Public 8-character link id.</param>
/// <param name="OwnerEmail">Email of the owning user; null for anonymous transfers; PII (TA-9.4).</param>
/// <param name="Status">Stored transfer status byte (see <c>TransferStatus</c>).</param>
/// <param name="TotalBytes">Total size of the files in bytes.</param>
/// <param name="CreatedAtUtc">UTC instant the transfer was created.</param>
public record RecentTransferItem(
    string LinkId,
    string? OwnerEmail,
    byte Status,
    long TotalBytes,
    DateTime CreatedAtUtc);
