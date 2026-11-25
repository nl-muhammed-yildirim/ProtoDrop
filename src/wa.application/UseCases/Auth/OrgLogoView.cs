using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>US-021-04: the owner's logo state — preview URL (30-min inline SAS, null when none), the plan's MAX_LOGO_SIZE and whether branding is on for the plan.</summary>
/// <param name="LogoUrl">30-minute inline read URL of the current logo; null when the user has none.</param>
/// <param name="MaxLogoSize">The plan's maximum logo size in bytes.</param>
/// <param name="BrandingEnabled">True when the user's plan includes branding.</param>
public sealed record OrgLogoView(Uri? LogoUrl, long MaxLogoSize, bool BrandingEnabled);
