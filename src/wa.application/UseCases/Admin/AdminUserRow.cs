namespace wa.application.UseCases.Admin;

/// <summary>T-011-06: one row in the admin users list (TA-4.2#22). Storage + count are computed, not stored.</summary>
/// <param name="Id">Identifier of the admin user row.</param>
/// <param name="Email">The email of the admin user row.</param>
/// <param name="PlanName">The plan name of the admin user row.</param>
/// <param name="StorageBytesUsed">The storage bytes used of the admin user row.</param>
/// <param name="ActiveTransferCount">The active transfer count of the admin user row.</param>
/// <param name="CreatedAtUtc">The created at UTC of the admin user row.</param>
public record AdminUserRow(
    Guid Id,
    string Email,
    string? PlanName,
    long StorageBytesUsed,
    int ActiveTransferCount,
    DateTime CreatedAtUtc);
