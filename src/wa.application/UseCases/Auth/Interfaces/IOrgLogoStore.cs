namespace wa.application.UseCases.Auth;

/// <summary>
/// US-021-04 (TA-2.3 / TA-4.2a): narrow write port for AppUser.OrgLogoBlobPath. Reads go through
/// <see cref="IAppUserStore.GetByIdAsync"/>; a separate port keeps IAppUserStore's many hand-rolled stubs untouched.
/// </summary>
public interface IOrgLogoStore
{
    /// <summary>Set (or clear, with null) the logo blob path of a non-deleted user. Returns false when no row matched (missing or soft-deleted).</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="blobPath">The blob path.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns><see langword="true"/> when the operation succeeded; otherwise <see langword="false"/>.</returns>
    Task<bool> SetPathAsync(Guid userId, string? blobPath, CancellationToken ct);
}
