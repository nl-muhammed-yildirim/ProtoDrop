namespace wa.application.Ports;

/// <summary>US-021-04 (TA-3.6): 30-minute inline read SAS on one org logo (avatars container, permissions r).</summary>
public interface IOrgLogoSasMinter
{
    /// <summary>Mints the org logo.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <returns>The URI.</returns>
    Uri MintOrgLogo(Guid userId);
}
