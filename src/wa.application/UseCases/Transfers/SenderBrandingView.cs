namespace wa.application.UseCases.Transfers;

/// <summary>US-021-04: what the gate lets through — the trimmed org name (or null) and whether the sender has a stored logo.</summary>
/// <param name="OrgName">Trimmed organization name that may be shown; null when none.</param>
/// <param name="HasLogo">True when the sender has a stored logo and branding is allowed for the plan.</param>
public sealed record SenderBrandingView(string? OrgName, bool HasLogo)
{
    /// <summary>The view for senders without branding: no organization name and no logo.</summary>
    public static readonly SenderBrandingView Empty = new(null, false);
}
