using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Auth;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): set a user's plan — read user + plan, capture oldPlanId BEFORE the write, then update.
/// The endpoint audits {oldPlanId,newPlanId} from the result (FR-011-5) — that is why oldPlanId is captured pre-update.
/// </summary>
public sealed class SetUserPlanCommandHandler : IRequestHandler<SetUserPlanCommand, SetUserPlanResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IAdminUserStore _adminUsers;

    /// <summary>Initializes a new instance of the <see cref="SetUserPlanCommandHandler"/> class.</summary>
    /// <param name="users">Store used to read the user and its current plan.</param>
    /// <param name="plans">Store used to check that the target plan exists.</param>
    /// <param name="adminUsers">Admin store that writes the new plan id.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public SetUserPlanCommandHandler(IAppUserStore users, IPlansStore plans, IAdminUserStore adminUsers)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _adminUsers = adminUsers ?? throw new ArgumentNullException(nameof(adminUsers));
    }

    /// <inheritdoc />
    public async Task<SetUserPlanResult> Handle(SetUserPlanCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate: no row (unknown OR soft-deleted) → NOT_FOUND (404 at the endpoint).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw SetUserPlanException.ForNotFound();

        // 2. Plan validation: IPlansStore.GetByIdAsync returns the row or null, never throws (LimitsStores.cs).
        var plan = await _plans.GetByIdAsync(request.PlanId, cancellationToken);
        if (plan is null)
            throw SetUserPlanException.ForPlanNotFound(); // Decision 2: 404 NOT_FOUND — id resolves to no row

        // 3. Capture oldPlanId BEFORE the write — the audit details {oldPlanId,newPlanId} depend on it (FR-011-5).
        var oldPlanId = user.PlanId;

        // 4. Single-column update via IAdminUserStore (T-011-07 DECISION: extend the existing admin port).
        var ok = await _adminUsers.SetPlanIdAsync(request.UserId, request.PlanId, cancellationToken);
        if (!ok)
            throw SetUserPlanException.ForNotFound(); // race where a concurrent force-delete committed — same not-found shape

        return new SetUserPlanResult(request.UserId, oldPlanId, request.PlanId);
    }
}
