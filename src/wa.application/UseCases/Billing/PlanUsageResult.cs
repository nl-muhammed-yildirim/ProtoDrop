using wa.application.Limits;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>ActiveTransfersAllowed null = unlimited (-1 sentinel). Storage has no sentinel (Part 2).</summary>
/// <param name="PlanCode">The plan code of the plan usage result.</param>
/// <param name="StorageUsedBytes">The storage used bytes of the plan usage result.</param>
/// <param name="StorageAllowedBytes">The storage allowed bytes of the plan usage result.</param>
/// <param name="ActiveTransfersUsed">The active transfers used of the plan usage result.</param>
/// <param name="ActiveTransfersAllowed">The active transfers allowed of the plan usage result.</param>
/// <param name="MaxTransferSize">The max transfer size of the plan usage result.</param>
/// <param name="MaxSingleFile">The max single file of the plan usage result.</param>
/// <param name="Scheduling">Whether scheduling of the plan usage result.</param>
/// <param name="Analytics">Whether analytics of the plan usage result.</param>
public sealed record PlanUsageResult(string PlanCode, long StorageUsedBytes, long StorageAllowedBytes, int ActiveTransfersUsed, int? ActiveTransfersAllowed, long MaxTransferSize, long MaxSingleFile, bool Scheduling = false, bool Analytics = false);
