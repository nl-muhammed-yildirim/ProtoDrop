using MediatR;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): PATCH /api/v1/admin/users/{id}?planId= — SuperAdmin-only plan write.
/// Limits take effect at the NEXT finalize (FR-BIL-001-4 "applies to new transfers") — no limit recompute here.
/// </summary>
public sealed record SetUserPlanCommand(Guid UserId, Guid PlanId) : IRequest<SetUserPlanResult>;

/// <summary>T-011-07: the write result — old + new plan ids so the endpoint can audit {oldPlanId,newPlanId} (FR-011-5).</summary>
public sealed record SetUserPlanResult(Guid UserId, Guid OldPlanId, Guid NewPlanId);

/// <summary>
/// T-011-07 (D-2): the single use-case exception for the set-plan write — house style: private ctor + static factories.
/// Both factories map to 404 NOT_FOUND (unknown user OR unknown planId — Decision 2). No new closed-list member (TA-4.1.3 stays at 18).
/// </summary>
public sealed class SetUserPlanException : Exception
{
    private SetUserPlanException(string message) : base(message) { }

    /// <summary>No row for the user id (unknown OR soft-deleted) — endpoint → 404 NOT_FOUND.</summary>
    public static SetUserPlanException ForNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound)); // title from the closed table — never "user not found"

    /// <summary>No row for the planId — endpoint → 404 NOT_FOUND (Decision 2: id resolves to no row = NOT_FOUND, not 400).</summary>
    public static SetUserPlanException ForPlanNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound));
}

/// <summary>
/// T-011-07 (TA-4.2#22 / FR-011-2): set a user's plan — read user + plan, capture oldPlanId BEFORE the write, then update.
/// The endpoint audits {oldPlanId,newPlanId} from the result (FR-011-5) — that is why oldPlanId is captured pre-update.
/// </summary>
public sealed class SetUserPlanCommandHandler : IRequestHandler<SetUserPlanCommand, SetUserPlanResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IAdminUserStore _adminUsers;

    public SetUserPlanCommandHandler(IAppUserStore users, IPlansStore plans, IAdminUserStore adminUsers)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _adminUsers = adminUsers ?? throw new ArgumentNullException(nameof(adminUsers));
    }

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
