using wa.application.Limits;

namespace wa.application.UseCases.Transfers;

/// <summary>F-PRF-001: the single sender-branding gate rule, shared by the recipient page (US-021-01) and the notification email (US-021-02).</summary>
public static class SenderBranding
{
    /// <summary>
    /// Branding to show, resolved from the sender's CURRENT profile and plan (EC-021-5: stored values are never touched).
    /// Guest sends, a missing owner row and plans without Branding yield an empty view. The logo does not depend on the org name.
    /// </summary>
    /// <param name="store">Store that reads the sender's organization name, logo path and plan.</param>
    /// <param name="features">Provider of the plan's feature toggles.</param>
    /// <param name="ownerAppUserId">The sending user; null for anonymous senders.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The trimmed organization name and the logo flag, or <see cref="SenderBrandingView.Empty"/> when branding does not apply.</returns>
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
    /// <param name="store">Store that reads the sender's organization name, logo path and plan.</param>
    /// <param name="features">Provider of the plan's feature toggles.</param>
    /// <param name="ownerAppUserId">The sending user; null for anonymous senders.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The trimmed organization name, or <see langword="null"/> when branding does not apply.</returns>
    public static async Task<string?> ResolveOrgNameAsync(
        ISenderBrandingStore store, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct) =>
        (await ResolveAsync(store, features, ownerAppUserId, ct)).OrgName;

    /// <summary>Email "From" line (FR-021-2): org null/blank → senderName unchanged; senderName blank → org; else "{org} — {senderName}" (U+2014, single spaces).</summary>
    /// <param name="senderName">Sender's name; may be blank.</param>
    /// <param name="orgName">Organization name; null or blank when branding does not apply.</param>
    /// <returns>The sender line: the sender name alone, the organization alone, or both separated by an em dash.</returns>
    public static string ComposeEmailSenderLine(string senderName, string? orgName)
    {
        if (string.IsNullOrWhiteSpace(orgName))
            return senderName;
        if (string.IsNullOrWhiteSpace(senderName))
            return orgName.Trim();
        return $"{orgName.Trim()} — {senderName}";
    }
}
