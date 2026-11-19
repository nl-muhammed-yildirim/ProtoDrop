namespace wa.application.UseCases.Auth;

/// <summary>
/// US-021-04 (TA-2.3 / TA-4.2a): narrow write port for AppUser.OrgLogoBlobPath. Reads go through
/// <see cref="IAppUserStore.GetByIdAsync"/>; a separate port keeps IAppUserStore's many hand-rolled stubs untouched.
/// </summary>
public interface IOrgLogoStore
{
    /// <summary>Set (or clear, with null) the logo blob path of a non-deleted user. A missing row is a no-op.</summary>
    Task SetPathAsync(Guid userId, string? blobPath, CancellationToken ct);
}
