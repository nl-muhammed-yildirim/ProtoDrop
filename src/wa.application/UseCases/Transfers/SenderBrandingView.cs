namespace wa.application.UseCases.Transfers;

/// <summary>US-021-04: what the gate lets through — the trimmed org name (or null) and whether the sender has a stored logo.</summary>
public sealed record SenderBrandingView(string? OrgName, bool HasLogo)
{
    public static readonly SenderBrandingView Empty = new(null, false);
}
