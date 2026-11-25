namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: one row in the admin users list (TA-4.2#22). Storage + count are computed, not stored.</summary>
public record AdminUserRow(
    Guid Id,
    string Email,
    string? PlanName,
    long StorageBytesUsed,
    int ActiveTransferCount,
    DateTime CreatedAtUtc);
