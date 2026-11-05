using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-07 (TA-4.2#22 / FR-011-2): PATCH /api/v1/admin/users/{id}?planId= — SuperAdmin-only plan write.
// The handler's contract: read user + plan, capture oldPlanId BEFORE the write, then update via IAdminUserStore.
// SetUserPlanException.ForNotFound / ForPlanNotFound → endpoint maps to 404 NOT_FOUND (Decision 2).
public class SetUserPlanCommandTests
{
    private readonly StubAppUserStore _users;
    private readonly StubPlansStore _plans;
    private readonly StubAdminUserStore _adminUsers;
    private readonly SetUserPlanCommandHandler _handler;

    public SetUserPlanCommandTests()
    {
        _users = new StubAppUserStore();
        _plans = new StubPlansStore();
        _adminUsers = new StubAdminUserStore();
        _handler = new SetUserPlanCommandHandler(_users, _plans, _adminUsers);
    }

    // ---- Exit check: unknown user → SetUserPlanException.ForNotFound (endpoint → 404 NOT_FOUND) ----

    [Fact]
    public async Task UserNotFound_Throws_ForNotFound()
    {
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _users.User = null; // no row for the user id

        await Assert.ThrowsAsync<SetUserPlanException>(async () =>
            await _handler.Handle(new SetUserPlanCommand(userId, planId), CancellationToken.None));

        // The store was NOT called — the user read failed first.
        Assert.Equal(0, _adminUsers.SetPlanCallCount);
    }

    // ---- Exit check: unknown planId → SetUserPlanException.ForPlanNotFound (Decision 2: 404 NOT_FOUND, not 400) ----

    [Fact]
    public async Task PlanNotFound_Throws_ForPlanNotFound()
    {
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _users.User = NewUser(userId, planId); // user exists
        _plans.Plan = null; // no row for the planId

        await Assert.ThrowsAsync<SetUserPlanException>(async () =>
            await _handler.Handle(new SetUserPlanCommand(userId, planId), CancellationToken.None));

        // The store was NOT called — the plan read failed first.
        Assert.Equal(0, _adminUsers.SetPlanCallCount);
    }

    // ---- Exit check: happy path — change plan Free → Pro sets AppUser.PlanId; result carries old + new ----

    [Fact]
    public async Task Success_SetsNewPlanId_ReturnsOldAndNew()
    {
        var userId = Guid.NewGuid();
        var freeId = Guid.NewGuid();
        var proId = Guid.NewGuid();
        _users.User = NewUser(userId, freeId); // user starts on Free
        _plans.Plan = new Plan { Id = proId }; // Pro plan exists

        var result = await _handler.Handle(new SetUserPlanCommand(userId, proId), CancellationToken.None);

        // The store was called with (userId, proId).
        Assert.Equal(1, _adminUsers.SetPlanCallCount);
        Assert.Equal(userId, _adminUsers.CapturedUserId);
        Assert.Equal(proId, _adminUsers.CapturedNewPlanId);

        // Result carries old + new plan ids (the endpoint audits {oldPlanId,newPlanId} from this).
        Assert.Equal(userId, result.UserId);
        Assert.Equal(freeId, result.OldPlanId);
        Assert.Equal(proId, result.NewPlanId);
    }

    // ---- Exit check: oldPlanId is read from the pre-update row and passed into the result (audit depends on it) ----

    [Fact]
    public async Task Success_ReadsOldPlanBeforeUpdate()
    {
        var userId = Guid.NewGuid();
        var freeId = Guid.NewGuid();
        var proId = Guid.NewGuid();
        _users.User = NewUser(userId, freeId); // user starts on Free
        _plans.Plan = new Plan { Id = proId }; // Pro plan exists

        var result = await _handler.Handle(new SetUserPlanCommand(userId, proId), CancellationToken.None);

        // oldPlanId is the pre-update value (freeId) — NOT the new plan id.
        Assert.Equal(freeId, result.OldPlanId);
        Assert.NotEqual(result.NewPlanId, result.OldPlanId); // old and new are distinct
    }

    // ---- Exit check: race — user read OK but SetPlanIdAsync returns false → ForNotFound (same not-found shape) ----

    [Fact]
    public async Task SetPlanIdAsync_ReturnsFalse_Throws_ForNotFound()
    {
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _users.User = NewUser(userId, Guid.NewGuid()); // user exists
        _plans.Plan = new Plan { Id = planId }; // plan exists
        _adminUsers.SetPlanResult = false; // race: the row vanished between read and write

        await Assert.ThrowsAsync<SetUserPlanException>(async () =>
            await _handler.Handle(new SetUserPlanCommand(userId, planId), CancellationToken.None));

        // The store WAS called (the race happened at the write).
        Assert.Equal(1, _adminUsers.SetPlanCallCount);
    }

    // ---- Helpers ----

    private static AppUser NewUser(Guid id, Guid planId) => new()
    {
        Id = id,
        Email = "ada@x.com",
        PlanId = planId,
        DeletedAtUtc = null // not soft-deleted — GetByIdAsync returns this row
    };

    // ---- Stubs (hand-written, no Moq — mirrors AdminDeleteTransferCommandTests + SearchTransfersQueryTests style) ----

    private sealed class StubAppUserStore : IAppUserStore
    {
        public AppUser? User { get; set; }

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) =>
            Task.FromResult(User);

        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;

        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(User is not null && User.Id == userId ? User : null);

        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class StubPlansStore : IPlansStore
    {
        public Plan? Plan { get; set; }

        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) =>
            Task.FromResult(Plan ?? throw new InvalidOperationException("Stub: no plan"));

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plan is not null && Plan.Id == planId ? Plan : null);
    }

    private sealed class StubAdminUserStore : IAdminUserStore
    {
        public int SetPlanCallCount { get; private set; }
        public Guid CapturedUserId { get; private set; }
        public Guid CapturedNewPlanId { get; private set; }
        public bool SetPlanResult { get; set; } = true;

        public Task<(List<AdminUserRow> Items, string? NextCursor)> GetUsersAsync(
            string? q,
            DateTime? afterUtc,
            Guid? afterIdExcluded,
            int limit,
            CancellationToken ct) =>
            Task.FromResult((new List<AdminUserRow>(), (string?)null));

        public Task<bool> SetPlanIdAsync(Guid userId, Guid newPlanId, CancellationToken ct)
        {
            SetPlanCallCount++;
            CapturedUserId = userId;
            CapturedNewPlanId = newPlanId;
            return Task.FromResult(SetPlanResult);
        }
    }
}
