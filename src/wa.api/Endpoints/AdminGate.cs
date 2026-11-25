using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.domain.Errors; // T-056-02: /health catch writes through ProblemWriter (closed code list)
using wa.api.Middleware;

namespace wa.api.Endpoints;

/// <summary>
/// CHORE-code-style-2 (TA-9.2): the admin / operator role gate that used to be Program.cs locals. Holds the two email sets read once from
/// configuration right after <c>Build()</c> and the <see cref="RequireAdminAsync"/> helper the admin and auth endpoints call.
/// </summary>
internal sealed class AdminGate
{
    private AdminGate(HashSet<string> adminEmails, HashSet<string> operatorEmails)
    {
        AdminEmails = adminEmails;
        OperatorEmails = operatorEmails;
    }

    /// <summary>Gets the lower-cased admin emails from <c>Wa:AdminEmails</c> (case-insensitive set).</summary>
    public IReadOnlySet<string> AdminEmails { get; }

    /// <summary>Gets the operator emails from <c>Wa:OperatorEmails</c> (case-insensitive set); empty means every admin is a SuperAdmin.</summary>
    public IReadOnlySet<string> OperatorEmails { get; }

    /// <summary>Reads the two email lists from configuration (called once, right after <c>Build()</c>, as before).</summary>
    /// <param name="configuration">The live application configuration.</param>
    /// <returns>The gate holding both sets.</returns>
    public static AdminGate FromConfiguration(IConfiguration configuration)
    {
        // T-011-01 (TA-9.2 / FR-011-1): role is derived PER REQUEST from the two config lists — same cookie, role flips when a list changes (EC-011-3).
        // Wa:AdminEmails = frozen key (D-18 dev seed "dev@example.com"); Wa:OperatorEmails = NEW optional key — absent/empty → every admin is SuperAdmin.
        var adminEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operatorEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in configuration["Wa:AdminEmails"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            adminEmails.Add(part);
        foreach (var part in configuration["Wa:OperatorEmails"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            operatorEmails.Add(part);
        return new AdminGate(adminEmails, operatorEmails);
    }

    // T-011-01 gate helper — the contract endpoints 20–23 call as their routes land (T-011-03…T-011-09). Session read → 401 UNAUTHENTICATED inline
    // (same idiom as /auth/me L578); unknown email OR not in Wa:AdminEmails → FORBIDDEN; SuperAdmin-only route with an Operator role → same FORBIDDEN.
    // Returns the role (US-047-03 email reveal). No audit row for a rejected gate — the suppression DELETE emits TelemetryEvents.AdminAction AFTER the gate passes (T-006-03).

    /// <summary>Resolves the session user's admin role or throws the closed-set 401 / 403 Problem.</summary>
    /// <param name="ctx">The current HTTP context (carries the session user id).</param>
    /// <param name="users">Store used to load the session user's email.</param>
    /// <param name="superadminOnly">True when only a SuperAdmin may pass.</param>
    /// <param name="ct">Cancellation token for the user lookup.</param>
    /// <returns>The caller's role.</returns>
    /// <exception cref="BusinessException">UNAUTHENTICATED without a session; FORBIDDEN when the email has no role or the route needs a SuperAdmin.</exception>
    public async Task<AdminRole> RequireAdminAsync(HttpContext ctx, IAppUserStore users, bool superadminOnly, CancellationToken ct)
    {
        if (ctx.Items[SessionMiddleware.SessionUserIdKey] is not Guid userId)
            throw new BusinessException(ProblemCodes.Unauthenticated, ProblemCodes.TitleOf(ProblemCodes.Unauthenticated));

        var user = await users.GetByIdAsync(userId, ct);
        var role = user?.Email is { } email ? AdminRolePolicy.Resolve(email.ToLowerInvariant(), AdminEmails, OperatorEmails) : null;
        if (role is null || (superadminOnly && role != AdminRole.SuperAdmin))
            throw new BusinessException(ProblemCodes.Forbidden, ProblemCodes.TitleOf(ProblemCodes.Forbidden));
        return role.Value;
    }
}
