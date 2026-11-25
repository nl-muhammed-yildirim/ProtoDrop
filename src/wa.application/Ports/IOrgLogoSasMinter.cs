namespace wa.application.Ports;

/// <summary>US-021-04 (TA-3.6): 30-minute inline read SAS on one org logo (avatars container, permissions r).</summary>
public interface IOrgLogoSasMinter
{
    Uri MintOrgLogo(Guid userId);
}
