namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: one row in the admin users list (TA-4.2#22). Storage + count are computed, not stored.</summary>
/// <param name="Id">Primary key of the user.</param>
/// <param name="Email">Sign-in email; PII (TA-9.4).</param>
/// <param name="PlanName">Display name of the user's plan; null when the plan row cannot be resolved.</param>
/// <param name="StorageBytesUsed">Bytes held by the user's active transfers, computed on read.</param>
/// <param name="ActiveTransferCount">Number of the user's active transfers, computed on read.</param>
/// <param name="CreatedAtUtc">UTC instant the account was created.</param>
public record AdminUserRow(
    Guid Id,
    string Email,
    string? PlanName,
    long StorageBytesUsed,
    int ActiveTransferCount,
    DateTime CreatedAtUtc);
