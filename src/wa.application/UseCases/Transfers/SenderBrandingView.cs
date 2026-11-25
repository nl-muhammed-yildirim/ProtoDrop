namespace wa.application.UseCases.Transfers;

/// <summary>US-021-04: what the gate lets through — the trimmed org name (or null) and whether the sender has a stored logo.</summary>
/// <param name="OrgName">The org name of the sender branding view.</param>
/// <param name="HasLogo">Whether has logo of the sender branding view.</param>
public sealed record SenderBrandingView(string? OrgName, bool HasLogo)
{
    /// <summary>The shared empty value.</summary>
    public static readonly SenderBrandingView Empty = new(null, false);
}
