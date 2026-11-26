using wa.application.UseCases.Admin; // T-011-01: AdminRolePolicy (gate + role field)
using wa.application.UseCases.Auth;
using wa.domain.Errors;
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

    /// <summary>Gets the admin emails from <c>Wa:AdminEmails</c> as written in configuration (matched case-insensitively).</summary>
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

    /// <summary>Resolves the session user's admin role or throws the closed-set 401 / 403 Problem.</summary>
    /// <remarks>
    /// T-011-01 gate helper for the admin endpoints. Session read gives 401 UNAUTHENTICATED; an unknown email, an email not in
    /// <c>Wa:AdminEmails</c>, an account whose email is not confirmed, or an Operator on a SuperAdmin-only route gives FORBIDDEN. Returns the role (US-047-03 email reveal).
    /// No audit row for a rejected gate: the suppression DELETE emits <c>TelemetryEvents.AdminAction</c> only AFTER the gate passes (T-006-03).
    /// </remarks>
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
        var role = user?.Email is { } email ? AdminRolePolicy.Resolve(email.ToLowerInvariant(), user.EmailConfirmed, AdminEmails, OperatorEmails) : null;
        if (role is null || (superadminOnly && role != AdminRole.SuperAdmin))
            throw new BusinessException(ProblemCodes.Forbidden, ProblemCodes.TitleOf(ProblemCodes.Forbidden));
        return role.Value;
    }
}
