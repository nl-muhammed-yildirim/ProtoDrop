using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>A logo upload rejected before anything was stored; <see cref="Code"/> is a closed ProblemCodes member.</summary>
public sealed class OrgLogoRejectedException : Exception
{
    public string Code { get; }

    private OrgLogoRejectedException(string code, string message) : base(message) => Code = code;

    /// <summary>FR-021-4: the sender's plan has no branding → 403 FORBIDDEN.</summary>
    public static OrgLogoRejectedException ForPlan() =>
        new(ProblemCodes.Forbidden, "Pro plan required.");

    /// <summary>AC-021-4: body larger than MAX_LOGO_SIZE → 422 LOGO_TOO_LARGE; the message names the limit.</summary>
    public static OrgLogoRejectedException ForTooLarge(long limit) =>
        new(ProblemCodes.LogoTooLarge, $"Logo exceeds the {FormatMegabytes(limit)} limit ({limit} bytes).");

    /// <summary>FR-021-5: content is not PNG/JPEG/WebP/SVG → 415 LOGO_TYPE_INVALID.</summary>
    public static OrgLogoRejectedException ForType() =>
        new(ProblemCodes.LogoTypeInvalid, "Logo must be a PNG, JPEG, WebP or SVG image.");

    private static string FormatMegabytes(long bytes) =>
        $"{bytes / (1024.0 * 1024.0):0.##} MB";
}
