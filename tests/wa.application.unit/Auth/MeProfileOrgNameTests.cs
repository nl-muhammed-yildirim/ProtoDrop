using System.Text.Json;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Auth;

// F-PRF-001 (FR-021-1 / FR-021-5, TA-4.2#13): PATCH /auth/me orgName — set, trim, clear, bounds, absent = unchanged.
public class MeProfileOrgNameTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Users _users = new();
    private readonly OrgNames _orgNames = new();
    private readonly UpdateProfileHandler _handler;
    private readonly Plan _plan = new() { Id = Guid.NewGuid(), Code = "pro", Name = "Pro" };

    public MeProfileOrgNameTests() : this(true) { }

    private MeProfileOrgNameTests(bool branding)
    {
        var plan = _plan;
        _users.User = new AppUser { Id = _userId, Email = "ada@example.com", DisplayName = "Ada", Theme = "system", PlanId = plan.Id, OrgName = "Old" };
        _orgNames.Users = _users;
        _handler = new UpdateProfileHandler(_users, new Plans(plan), _orgNames, new StubPlanContext(branding));
    }

    private Task<GetProfileResult> Patch(OrgNamePatch org) =>
        _handler.Handle(new UpdateProfileCommand(_userId, null, null, org), CancellationToken.None);

    [Fact]
    public async Task Set_StoresValue()
    {
        await Patch(OrgNamePatch.Of("Studio Nova"));
        Assert.Equal("Studio Nova", _users.User!.OrgName);
    }

    [Fact]
    public async Task Set_IsTrimmed()
    {
        await Patch(OrgNamePatch.Of("  Studio Nova \t"));
        Assert.Equal("Studio Nova", _users.User!.OrgName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task BlankOrNull_ClearsToNull(string? value)
    {
        await Patch(OrgNamePatch.Of(value));
        Assert.Null(_users.User!.OrgName);
        Assert.Equal([null], _orgNames.Writes);
    }

    [Fact]
    public async Task ExactlyMaxLength_Accepted()
    {
        var name = new string('a', AppUser.OrgNameMaxLength);
        await Patch(OrgNamePatch.Of(name));
        Assert.Equal(name, _users.User!.OrgName);
    }

    [Fact]
    public async Task OverMaxLength_Rejected_NothingWritten()
    {
        await Assert.ThrowsAsync<MeProfileException>(() => Patch(OrgNamePatch.Of(new string('a', AppUser.OrgNameMaxLength + 1))));
        Assert.Empty(_orgNames.Writes);
        Assert.Equal("Old", _users.User!.OrgName);
    }

    [Theory]
    [InlineData("Studio\nNova")]
    [InlineData("Studio\rNova")]
    [InlineData("Studio Nova\n")]
    public async Task LineBreak_Rejected(string value)
    {
        var ex = await Assert.ThrowsAsync<MeProfileException>(() => Patch(OrgNamePatch.Of(value)));
        Assert.Contains("orgName", ex.Message);
        Assert.Empty(_orgNames.Writes);
    }

    [Fact]
    public async Task Free_SetNonEmpty_Rejected403_NothingStored()
    {
        var free = new MeProfileOrgNameTests(false);
        var ex = await Assert.ThrowsAsync<OrgLogoRejectedException>(() => free.Patch(OrgNamePatch.Of("Studio Nova")));
        Assert.Equal(wa.domain.Errors.ProblemCodes.Forbidden, ex.Code);
        Assert.Empty(free._orgNames.Writes);
        Assert.Equal("Old", free._users.User!.OrgName);
    }

    [Fact]
    public async Task Free_Clear_Allowed()
    {
        var free = new MeProfileOrgNameTests(false);
        await free.Patch(OrgNamePatch.Of("  "));
        Assert.Null(free._users.User!.OrgName);
        Assert.Equal([null], free._orgNames.Writes);
    }

    [Fact]
    public async Task Pro_SetNonEmpty_Stored()
    {
        await Patch(OrgNamePatch.Of("Studio Nova"));
        Assert.Equal(["Studio Nova"], _orgNames.Writes);
    }

    [Fact]
    public async Task Absent_LeavesOrgNameUnchanged()
    {
        await _handler.Handle(new UpdateProfileCommand(_userId, "Grace", null), CancellationToken.None);
        Assert.Equal("Old", _users.User!.OrgName);
        Assert.Empty(_orgNames.Writes);
    }

    [Fact]
    public async Task OnlyOrgName_IsNotAnEmptyPatch()
    {
        await Patch(OrgNamePatch.Of("X")); // must not throw ForEmptyPatch
        Assert.Equal("X", _users.User!.OrgName);
    }

    [Fact]
    public void Json_AbsentIsNotSet_NullIsSet_StringIsSet()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.False(JsonSerializer.Deserialize<UpdateProfileRequest>("""{"displayName":"A"}""", web)!.OrgName.IsSet);

        var asNull = JsonSerializer.Deserialize<UpdateProfileRequest>("""{"orgName":null}""", web)!.OrgName;
        Assert.True(asNull.IsSet);
        Assert.Null(asNull.Value);

        var asString = JsonSerializer.Deserialize<UpdateProfileRequest>("""{"orgName":"Nova"}""", web)!.OrgName;
        Assert.True(asString.IsSet);
        Assert.Equal("Nova", asString.Value);
    }

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
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class OrgNames : IOrgNameStore
    {
        public Users? Users { get; set; }
        public List<string?> Writes { get; } = [];
        public Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct)
        {
            Writes.Add(orgName);
            Users!.User!.OrgName = orgName;
            return Task.CompletedTask;
        }
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
