using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.application.unit.EmailSettings;

namespace wa.application.unit.Auth;

// F-XCT-003 (AC-046-1, FR-046-2 / EC-046-2): PATCH/GET /auth/me emailNotifications — additive field, effective value.
public class MeProfileEmailNotificationsTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Users _users = new();
    private readonly StubEmailNotificationsStore _emailNotifications = new();
    private readonly UpdateProfileHandler _handler;
    private static readonly Plan PlanOf = new() { Id = Guid.NewGuid(), Code = "pro", Name = "Pro" };

    public MeProfileEmailNotificationsTests()
    {
        var plan = PlanOf;
        _users.User = new AppUser { Id = _userId, Email = "ada@example.com", DisplayName = "Ada", Theme = "system", PlanId = plan.Id };
        // Mirror the column onto the domain object so ResolveResult reflects the write (real store idiom).
        _emailNotifications.OnSet = (id, v) => _users.User!.EmailNotificationsEnabled = v;
        _handler = new UpdateProfileHandler(_users, new Plans(plan), new OrgNames(), new StubPlanContext(true), _emailNotifications);
    }

    private Task<GetProfileResult> Patch(bool? emailNotifications, string? theme = null) =>
        _handler.Handle(new UpdateProfileCommand(_userId, null, theme, default, emailNotifications), CancellationToken.None);

    [Fact]
    public async Task Patch_False_WritesOnce_ResultFalse() // AC-046-1 step 1: the switch off persists and echoes back
    {
        var result = await Patch(false);

        Assert.Equal([(_userId, false)], _emailNotifications.Writes);
        Assert.False(result.EmailNotifications);
    }

    [Fact]
    public async Task Patch_True_WritesTrue_ResultTrue() // AC-046-1 scenario 2: turned back on → sent again
    {
        var result = await Patch(true);

        Assert.Equal([(_userId, true)], _emailNotifications.Writes);
        Assert.True(result.EmailNotifications);
    }

    [Fact]
    public async Task Patch_ThemeOnly_DoesNotWriteSetting() // FR-046-2 additive: absent leaves the column alone
    {
        var result = await Patch(null, theme: "dark");

        Assert.Empty(_emailNotifications.Writes);
        Assert.Null(_users.User!.EmailNotificationsEnabled); // never touched → still null (on)
        Assert.True(result.EmailNotifications); // effective value of a NULL column is true
    }

    [Fact]
    public async Task Patch_OnlyEmailNotifications_IsNotEmptyPatch() // the field counts toward the empty-PATCH rule
    {
        var result = await Patch(false); // must not throw MeProfileException.ForEmptyPatch

        Assert.False(result.EmailNotifications);
    }

    [Fact]
    public async Task Patch_AllFieldsAbsent_StillEmptyPatchValidation()
    {
        await Assert.ThrowsAsync<MeProfileException>(async () =>
            await _handler.Handle(new UpdateProfileCommand(_userId, null, null), CancellationToken.None));
        Assert.Empty(_emailNotifications.Writes);
    }

    [Fact]
    public async Task GetProfile_NullColumn_ReturnsTrue() // FR-046-2 / EC-046-2: never touched = on (fresh row after re-signup)
    {
        var result = await new GetProfileHandler(_users, new Plans(PlanOf)).Handle(new GetProfileCommand(_userId), CancellationToken.None);

        Assert.Null(_users.User!.EmailNotificationsEnabled);
        Assert.True(result.EmailNotifications);
    }

    [Fact]
    public async Task GetProfile_False_ReturnsFalse() // FR-046-2: the stored false is echoed verbatim
    {
        _emailNotifications.Stored[_userId] = false;
        _users.User!.EmailNotificationsEnabled = false;

        var result = await new GetProfileHandler(_users, new Plans(PlanOf)).Handle(new GetProfileCommand(_userId), CancellationToken.None);

        Assert.False(result.EmailNotifications);
    }

    // ---- Stubs copied from MeProfileOrgNameTests (Users/Plans/StubPlanContext pattern) + the shared port stub ----

    private sealed class Users : IAppUserStore
    {
        public AppUser? User { get; set; }
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(User is not null && User.Id == userId ? User : null);
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult(User);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct)
        {
            if (displayName is not null) User!.DisplayName = displayName;
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmEmailAndClearPasswordAsync(Guid userId, DateTime nowUtc, CancellationToken ct) => Task.FromResult(true);
        public Task<DateTime?> GetSessionsValidAfterUtcAsync(Guid userId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class OrgNames : IOrgNameStore
    {
        public Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Plans(Plan plan) : IPlansStore
    {
        public Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken) => Task.FromResult(plan);
        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken) => Task.FromResult<Plan?>(plan);
    }

    private sealed class StubPlanContext(bool branding) : IPlanContextAccessor
    {
        public Task<PlanContext> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanContext(branding ? "pro" : "free", new LimitsRecord(), new PlanFeatures { Branding = branding }));
    }
}
