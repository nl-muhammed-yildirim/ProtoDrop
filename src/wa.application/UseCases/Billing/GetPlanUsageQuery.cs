using MediatR;
using wa.application.UseCases.Transfers;

namespace wa.application.UseCases.Billing;

/// <summary>US-020-03 (FR-020-3 / AC-020-3): current plan + live usage vs. PlanContext limits for the plan screen. Persists nothing.</summary>
public sealed record GetPlanUsageQuery(Guid UserId) : IRequest<PlanUsageResult>;
