namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: one row in the recent-transfers list (last 20 by CreatedAtUtc desc).</summary>
/// <param name="LinkId">Identifier of the link of the recent transfer item.</param>
/// <param name="OwnerEmail">The owner email of the recent transfer item.</param>
/// <param name="Status">The status of the recent transfer item.</param>
/// <param name="TotalBytes">The total bytes of the recent transfer item.</param>
/// <param name="CreatedAtUtc">The created at UTC of the recent transfer item.</param>
public record RecentTransferItem(
    string LinkId,
    string? OwnerEmail,
    byte Status,
    long TotalBytes,
    DateTime CreatedAtUtc);
