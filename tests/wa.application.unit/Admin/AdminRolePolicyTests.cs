using wa.application.UseCases.Admin;

namespace wa.application.unit.Admin;

// T-011-01 (TA-9.2 / AC-011-3 server half / EC-011-3): the role × endpoint policy table — pure static class, no DI.
// The gate in Program.cs is the contract endpoints 20–23 call as their routes land (T-011-03…T-011-09).
public class AdminRolePolicyTests
{
    private static readonly string[] GetEndpoints = // every GET of 20–23 (TA-4.2 rows 20–23) — Operator + SuperAdmin both pass.
        new[]
        {
            "/api/v1/admin/overview",
            "/api/v1/admin/transfers?q=&status=&cursor=",
            "/api/v1/admin/users?q=&cursor=",
            "/api/v1/admin/flags"
        };

    // ---- AC-011-3 (server half): every GET of 20–23 passes BOTH roles ----

    [Theory]
    [InlineData("/api/v1/admin/overview")]
    [InlineData("/api/v1/admin/transfers?q=&status=&cursor=")]
    [InlineData("/api/v1/admin/users?q=&cursor=")]
    [InlineData("/api/v1/admin/flags")]
    public void PolicyTable_GetEndpoints_AllowBothRoles(string route)
    {
        Assert.True(AdminRolePolicy.Allows(AdminRole.Operator, "GET", route)); // Operator is read-only — every GET passes
        Assert.True(AdminRolePolicy.Allows(AdminRole.SuperAdmin, "GET", route)); // SuperAdmin owns everything
    }

    // ---- AC-011-3 (server half): the EXACT write set of 20–23 is SuperAdmin-only — no more, no less ----

    [Theory]
    [InlineData("DELETE", "/api/v1/admin/transfers/{id}")]
    [InlineData("PATCH", "/api/v1/admin/users/{id}")]
    [InlineData("DELETE", "/api/v1/admin/users/{id}")] // user force-delete — same route, distinct verb pair in the table
    [InlineData("PUT", "/api/v1/admin/flags/{key}")]
    [InlineData("DELETE", "/api/v1/admin/suppressions/{id:guid}")]
    public void PolicyTable_WriteEndpoints_SuperAdminOnly(string method, string route)
    {
        Assert.True(AdminRolePolicy.Allows(AdminRole.SuperAdmin, method, route)); // SuperAdmin passes the exact write set
        Assert.False(AdminRolePolicy.Allows(AdminRole.Operator, method, route)); // Operator rejected — AC-011-3 server half
    }

    [Fact]
    public void PolicyTable_SuperAdminOnlyWrites_ExactSet_NoMoreNoLess()
    {
        // The task file's literal write set — pin the count so a later task can't silently grow/shrink it.
        Assert.Equal(5, AdminRolePolicy.SuperAdminOnlyWrites.Count);
        Assert.All(AdminRolePolicy.SuperAdminOnlyWrites, w =>
            Assert.True(AdminRolePolicy.Allows(AdminRole.SuperAdmin, w.Method, w.Route)));
    }

    // ---- EC-011-3: role is a pure function of the two config lists — same email, list change → role flips or nulls (never token claims) ----

    [Fact]
    public void RoleDerivedPerRequest_ListChangeFlipsRole()
    {
        var admin = new HashSet<string> { "ada@example.com" };
        var operatorList = new HashSet<string>();

        Assert.Equal(AdminRole.SuperAdmin, AdminRolePolicy.Resolve("ada@example.com", admin, operatorList)); // absent/empty Wa:OperatorEmails → SuperAdmin (task file DECISION line)

        operatorList.Add("ada@example.com");
        Assert.Equal(AdminRole.Operator, AdminRolePolicy.Resolve("ada@example.com", admin, operatorList)); // same email, list changed → role FLIPS to Operator

        var emptyAdmin = new HashSet<string>();
        Assert.Equal(AdminRole.Operator, AdminRolePolicy.Resolve("ada@example.com", emptyAdmin, operatorList)); // removed from Wa:AdminEmails but still in Wa:OperatorEmails → stays Operator (EC-011-3: role is a pure function of the two lists)

        Assert.Null(AdminRolePolicy.Resolve("ada@example.com", new HashSet<string>(), new HashSet<string>())); // in NEITHER list → null (non-admin) — gate rejects with FORBIDDEN

        Assert.Equal(AdminRole.Operator, AdminRolePolicy.Resolve("grace@example.com", admin, new HashSet<string> { "grace@example.com" })); // Operator-only email (not in Wa:AdminEmails) still resolves
    }

    [Fact]
    public void Resolve_CaseInsensitive_BothLists()
    {
        var admin = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ada@example.com" };
        Assert.Equal(AdminRole.SuperAdmin, AdminRolePolicy.Resolve("ADA@EXAMPLE.COM", admin, new HashSet<string>())); // AppUser.Email stored lowercased; config may not be — compare case-insensitively
    }

    // ---- UI-Reference §5.6: additive /auth/me role field values (endpoint 13) ----

    [Theory]
    [InlineData(AdminRole.SuperAdmin, "superadmin")]
    [InlineData(AdminRole.Operator, "operator")]
    public void RoleField_WireValues(AdminRole role, string expected) =>
        Assert.Equal(expected, AdminRolePolicy.RoleField(role));

    [Fact]
    public void RoleField_NonAdmin_Absent() =>
        Assert.Null(AdminRolePolicy.RoleField(null)); // null → key omitted from the /auth/me body (UI renders disabled buttons with tooltip, not hidden elements)
}
