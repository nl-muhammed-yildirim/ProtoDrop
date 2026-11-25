namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: one row in the recent-transfers list (last 20 by CreatedAtUtc desc).</summary>
public record RecentTransferItem(
    string LinkId,
    string? OwnerEmail,
    byte Status,
    long TotalBytes,
    DateTime CreatedAtUtc);
