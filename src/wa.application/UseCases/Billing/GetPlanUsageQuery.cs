using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>US-020-03 (FR-020-3 / AC-020-3): current plan + live usage vs. PlanContext limits for the plan screen. Persists nothing.</summary>
public sealed record GetPlanUsageQuery(Guid UserId) : IRequest<PlanUsageResult>;

/// <summary>ActiveTransfersAllowed null = unlimited (-1 sentinel). Storage has no sentinel (Part 2).</summary>
public sealed record PlanUsageResult(string PlanCode, long StorageUsedBytes, long StorageAllowedBytes, int ActiveTransfersUsed, int? ActiveTransfersAllowed, long MaxTransferSize, long MaxSingleFile, bool Scheduling = false);

public sealed class GetPlanUsageHandler : IRequestHandler<GetPlanUsageQuery, PlanUsageResult>
{
    private readonly IPlanContextAccessor _planContext;
    private readonly ITransferStore _transfers;

    public GetPlanUsageHandler(IPlanContextAccessor planContext, ITransferStore transfers)
    {
        _planContext = planContext ?? throw new ArgumentNullException(nameof(planContext));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
    }

    public async Task<PlanUsageResult> Handle(GetPlanUsageQuery request, CancellationToken ct)
    {
        var plan = await _planContext.GetAsync(ct);   // FR-020-1: the only plan read

        // Sequential: the singleton store shares one WaDbContext.
        var usedBytes = await _transfers.GetActiveStorageBytesAsync(request.UserId, ct);
        var activeCount = await _transfers.GetActiveTransferCountAsync(request.UserId, ct);

        return new PlanUsageResult(
            plan.PlanCode,
            usedBytes,
            plan.Limits.StorageQuota,
            activeCount,
            plan.Limits.IsActiveTransfersMaxInfinite() ? null : plan.Limits.ActiveTransfersMax,
            plan.Limits.MaxTransferSize,
            plan.Limits.MaxSingleFile,
            plan.Features.Scheduling);   // US-022-01 (FR-022-3): lets the SPA disable the schedule control on Free
    }
}
