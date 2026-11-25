namespace wa.application.Ports;

/// <summary>US-021-04 (TA-3.6): 30-minute inline read SAS on one org logo (avatars container, permissions r).</summary>
public interface IOrgLogoSasMinter
{
    /// <summary>Mints a 30-minute inline read URL for a user's organization logo.</summary>
    /// <param name="userId">The user who owns the logo.</param>
    /// <returns>An absolute read-only SAS URL for the logo blob.</returns>
    Uri MintOrgLogo(Guid userId);
}
