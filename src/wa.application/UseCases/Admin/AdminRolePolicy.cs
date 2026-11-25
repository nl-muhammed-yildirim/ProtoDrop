namespace wa.application.UseCases.Admin;

/// <summary>T-011-01 (TA-9.2): role × endpoint policy — pure static class, no DI (unit-testable). The gate in Program.cs is the contract endpoints 20–23 call.</summary>
public static class AdminRolePolicy
{
    /// <summary>The EXACT SuperAdmin-only write set of endpoints 20–23 (task file) — no more, no less. Reads = everything else.</summary>
    public static IReadOnlyList<(string Method, string Route)> SuperAdminOnlyWrites { get; } = new List<(string Method, string Route)>
    {
        ("DELETE", "/api/v1/admin/transfers/{id}"),
        ("PATCH",  "/api/v1/admin/users/{id}"),
        ("DELETE", "/api/v1/admin/users/{id}"), // user force-delete
        ("PUT",    "/api/v1/admin/flags/{key}"),
        ("DELETE", "/api/v1/admin/suppressions/{id:guid}")
    };

    /// <summary>EC-011-3: the role is a pure function of the two config lists — same email, list membership change → role flips or nulls (never token claims).</summary>
    /// <param name="lowercasedEmail">The signed-in user's email, already lowercased.</param>
    /// <param name="adminEmails">Configured emails that get the SuperAdmin role.</param>
    /// <param name="operatorEmails">Configured emails that get the read-only Operator role; checked first.</param>
    /// <returns>The role, or <see langword="null"/> when the email is on neither list (not an admin).</returns>
    public static AdminRole? Resolve(string lowercasedEmail, IReadOnlySet<string> adminEmails, IReadOnlySet<string> operatorEmails) =>
        operatorEmails.Contains(lowercasedEmail) ? AdminRole.Operator :
        adminEmails.Contains(lowercasedEmail) ? AdminRole.SuperAdmin : null;

    /// <summary>AC-011-3 (server half): every GET of 20–23 passes both roles; the exact write set above is SuperAdmin-only.</summary>
    /// <param name="role">The caller's admin role.</param>
    /// <param name="method">HTTP method of the request.</param>
    /// <param name="route">Route template of the request (reserved; the decision currently depends on role and method only).</param>
    /// <returns><see langword="true"/> for SuperAdmin on every request and for Operator on GET and HEAD requests; otherwise <see langword="false"/>.</returns>
    public static bool Allows(AdminRole role, string method, string route) =>
        role == AdminRole.SuperAdmin || IsReadOnly(method);

    /// <summary>T-011-01 (UI-Reference §5.6): the additive /auth/me role field — "superadmin" or "operator"; null for non-admin → omitted on the wire, so the UI renders disabled buttons with tooltip instead of hidden elements.</summary>
    /// <param name="role">The resolved role, or null for a non-admin.</param>
    /// <returns><c>superadmin</c>, <c>operator</c>, or null for a non-admin.</returns>
    public static string? RoleField(AdminRole? role) => role switch
    {
        AdminRole.SuperAdmin => "superadmin",
        AdminRole.Operator => "operator",
        _ => null
    };

    private static bool IsReadOnly(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
}
