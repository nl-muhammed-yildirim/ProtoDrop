namespace wa.application.UseCases.Auth;

/// <summary>
/// F-PRF-001 (FR-021-1 / TA-2.3): narrow write port for AppUser.OrgName. Reads go through
/// <see cref="IAppUserStore.GetByIdAsync"/>; a separate port keeps IAppUserStore's many hand-rolled stubs untouched.
/// </summary>
public interface IOrgNameStore
{
    /// <summary>Set (or clear, with null) the org name of a non-deleted user. A missing row is a no-op.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="orgName">The org name.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    Task SetOrgNameAsync(Guid userId, string? orgName, CancellationToken ct);
}
