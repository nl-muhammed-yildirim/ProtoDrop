using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04: the owner's logo state — preview URL (30-min inline SAS, null when none), the plan's MAX_LOGO_SIZE and whether branding is on for the plan.</summary>
public sealed record OrgLogoView(Uri? LogoUrl, long MaxLogoSize, bool BrandingEnabled);
