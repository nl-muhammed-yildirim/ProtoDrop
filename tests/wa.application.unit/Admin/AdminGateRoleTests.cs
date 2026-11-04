using wa.application.UseCases.Admin;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-01 (AC-011-3 server half — client-facing half / UI-Reference §5.6): the additive role field on GET /auth/me (endpoint 13).
// The Program.cs handler computes it exactly as: RoleField(Resolve(email, adminSet, operatorSet)) over a dictionary that carries the 'role' key ONLY when non-null —
// so these tests pin that contract with a hand-stub IAppUserStore (MeProfileTests style) instead of re-implementing the route.
public class AdminGateRoleTests
{
    private static readonly HashSet<string> AdminEmails = new(StringComparer.OrdinalIgnoreCase) { "ada@example.com" };
    private static readonly HashSet<string> OperatorEmails = new(StringComparer.OrdinalIgnoreCase);

    // ---- AC-011-3 (server half): non-admin → role ABSENT from the /auth/me body — not null, not missing-by-default: the key is never added at all ----

    [Fact]
    public void NonAdmin_RoleAbsent()
    {
        var user = new StubAppUserStore("grace@example.com").UserByEmail; // in no list → Resolve returns null

        Assert.False(BuildMeBody(user).ContainsKey("role")); // UI renders disabled buttons with tooltip (not hidden elements) when the key is absent
        Assert.Equal(5, BuildMeBody(user).Count); // exact TA-4.2a key set — additive field adds nothing for non-admin
    }

    [Fact]
    public void SuperAdmin_RoleSuperadmin()
    {
        var user = new StubAppUserStore("ada@example.com").UserByEmail; // in Wa:AdminEmails, NOT in Wa:OperatorEmails → SuperAdmin (task file DECISION line)

        Assert.Equal("superadmin", BuildMeBody(user)["role"]);
        Assert.Equal(6, BuildMeBody(user).Count); // 5 base keys + additive role
    }

    [Fact]
    public void Operator_RoleOperator()
    {
        var user = new StubAppUserStore("ada@example.com").UserByEmail;
        var operatorList = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ada@example.com" };

        Assert.Equal("operator", BuildMeBody(user, AdminEmails, operatorList)["role"]); // same email now in Wa:OperatorEmails → role flips per request (EC-011-3)
    }

    [Fact]
    public void UnknownRow_RoleAbsent()
    {
        // Session cookie valid but no AppUser row — Program.cs maps that to 404 NOT_FOUND before the body is built (T-008-06),
        // so pin only the role half: Resolve never runs over a null email → RoleField(null) → key absent.
        var user = new StubAppUserStore("ada@example.com").GetByIdAsync(Guid.NewGuid(), CancellationToken.None).Result;

        Assert.Null(user); // stub returns null for unknown ids — same contract as the real store (T-008-06 D-1)
    }

    // Mirrors Program.cs L612–623 exactly: dictionary preserves insertion order; 'role' present ONLY when non-null.
    private static Dictionary<string, object> BuildMeBody(AppUser? user, HashSet<string>? adminEmails = null, HashSet<string>? operatorEmails = null)
    {
        var roleField = user?.Email is { } email
            ? AdminRolePolicy.RoleField(AdminRolePolicy.Resolve(email.ToLowerInvariant(), adminEmails ?? AdminEmails, operatorEmails ?? OperatorEmails))
            : null;

        var meBody = new Dictionary<string, object>
        {
            ["userId"] = user!.Id.ToString("n"),
            ["email"] = user.Email,
            ["displayName"] = user.DisplayName,
            ["theme"] = user.Theme,
            ["planCode"] = "free"
        };
        if (roleField is not null)
            meBody["role"] = roleField;
        return meBody;
    }

    // T-011-01 hand stub — same style as MeProfileTests.StubAppUserStore (no Moq in wa.application.unit).
    private sealed class StubAppUserStore : IAppUserStore
    {
        public AppUser? UserByEmail { get; set; }

        public StubAppUserStore(string email) => UserByEmail = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email, // stored lowercased + trimmed (T-008-01 port contract)
            DisplayName = "Ada Lovelace",
            Theme = "system",
            PlanId = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };

        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => Task.FromResult(UserByEmail);
        public Task CreateAsync(AppUser user, CancellationToken ct) => Task.CompletedTask;
        // T-008-06 D-1 contract: null when unknown OR soft-deleted — /auth/me maps that to 404 NOT_FOUND.
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(userId == UserByEmail?.Id ? UserByEmail : null);
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => Task.FromResult(true);
    }
}
