using wa.application.Limits;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>The plan screen's current plan and live usage versus limits. <paramref name="ActiveTransfersAllowed"/> is null when unlimited; storage has no unlimited sentinel.</summary>
/// <param name="PlanCode">Code of the user's effective plan.</param>
/// <param name="StorageUsedBytes">Bytes currently held by the user's active transfers.</param>
/// <param name="StorageAllowedBytes">Plan storage quota in bytes.</param>
/// <param name="ActiveTransfersUsed">Number of the user's active transfers.</param>
/// <param name="ActiveTransfersAllowed">Plan cap on active transfers; null when unlimited.</param>
/// <param name="MaxTransferSize">Plan limit for one transfer in bytes.</param>
/// <param name="MaxSingleFile">Plan limit for one file in bytes.</param>
/// <param name="Scheduling">True when the plan may schedule sends.</param>
/// <param name="Analytics">True when the plan includes download analytics.</param>
public sealed record PlanUsageResult(string PlanCode, long StorageUsedBytes, long StorageAllowedBytes, int ActiveTransfersUsed, int? ActiveTransfersAllowed, long MaxTransferSize, long MaxSingleFile, bool Scheduling = false, bool Analytics = false);
