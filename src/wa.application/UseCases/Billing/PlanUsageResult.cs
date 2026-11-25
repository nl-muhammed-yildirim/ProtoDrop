using wa.application.Limits;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>ActiveTransfersAllowed null = unlimited (-1 sentinel). Storage has no sentinel (Part 2).</summary>
public sealed record PlanUsageResult(string PlanCode, long StorageUsedBytes, long StorageAllowedBytes, int ActiveTransfersUsed, int? ActiveTransfersAllowed, long MaxTransferSize, long MaxSingleFile, bool Scheduling = false, bool Analytics = false);
