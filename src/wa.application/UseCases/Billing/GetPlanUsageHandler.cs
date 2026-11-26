using MediatR;
using wa.application.Limits;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>Handles <see cref="GetPlanUsageQuery"/>: combines the request's plan context with the user's live storage and active-transfer counts.</summary>
public sealed class GetPlanUsageHandler : IRequestHandler<GetPlanUsageQuery, PlanUsageResult>
{
    private readonly IPlanContextAccessor _planContext;
    private readonly ITransferStore _transfers;

    /// <summary>Initializes a new instance of the <see cref="GetPlanUsageHandler"/> class.</summary>
    /// <param name="planContext">Accessor for the request's plan, limits and features.</param>
    /// <param name="transfers">Store that counts the user's active transfers and bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public GetPlanUsageHandler(IPlanContextAccessor planContext, ITransferStore transfers)
    {
        _planContext = planContext ?? throw new ArgumentNullException(nameof(planContext));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
    }

    /// <inheritdoc />
    public async Task<PlanUsageResult> Handle(GetPlanUsageQuery request, CancellationToken ct)
    {
        var plan = await _planContext.GetAsync(ct);   // FR-020-1: the only plan read

        // Sequential keeps one DB round-trip at a time per request.
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
            plan.Features.Scheduling,   // US-022-01 (FR-022-3): lets the SPA disable the schedule control on Free
            plan.Features.Analytics);   // US-023-01 (FR-023-5): lets the SPA skip the analytics call on Free
    }
}
