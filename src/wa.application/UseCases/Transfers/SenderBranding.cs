using wa.application.Limits;

namespace wa.application.UseCases.Transfers;

/// <summary>US-021-04: what the gate lets through — the trimmed org name (or null) and whether the sender has a stored logo.</summary>
public sealed record SenderBrandingView(string? OrgName, bool HasLogo)
{
    public static readonly SenderBrandingView Empty = new(null, false);
}

/// <summary>F-PRF-001: the single sender-branding gate rule, shared by the recipient page (US-021-01) and the notification email (US-021-02).</summary>
public static class SenderBranding
{
    /// <summary>
    /// Branding to show, resolved from the sender's CURRENT profile and plan (EC-021-5: stored values are never touched).
    /// Guest sends, a missing owner row and plans without Branding yield an empty view. The logo does not depend on the org name.
    /// </summary>
    public static async Task<SenderBrandingView> ResolveAsync(
        ISenderBrandingStore store, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is null)
            return SenderBrandingView.Empty;

        var row = await store.GetAsync(ownerAppUserId.Value, ct);
        if (row is null)
            return SenderBrandingView.Empty;

        var hasName = !string.IsNullOrWhiteSpace(row.OrgName);
        var hasLogo = !string.IsNullOrEmpty(row.OrgLogoBlobPath);
        if (!hasName && !hasLogo)
            return SenderBrandingView.Empty;

        var resolved = await features.Resolve(row.PlanCode, ct);
        if (!resolved.Branding)
            return SenderBrandingView.Empty;

        return new SenderBrandingView(hasName ? row.OrgName!.Trim() : null, hasLogo);
    }

    /// <summary>
    /// Org name to show, resolved from the sender's CURRENT profile and plan.
    /// Guest sends, blank org names and plans without Branding yield null.
    /// </summary>
    public static async Task<string?> ResolveOrgNameAsync(
        ISenderBrandingStore store, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct) =>
        (await ResolveAsync(store, features, ownerAppUserId, ct)).OrgName;

    /// <summary>Email "From" line (FR-021-2): org null/blank → senderName unchanged; senderName blank → org; else "{org} — {senderName}" (U+2014, single spaces).</summary>
    public static string ComposeEmailSenderLine(string senderName, string? orgName)
    {
        if (string.IsNullOrWhiteSpace(orgName))
            return senderName;
        if (string.IsNullOrWhiteSpace(senderName))
            return orgName.Trim();
        return $"{orgName.Trim()} — {senderName}";
    }
}
