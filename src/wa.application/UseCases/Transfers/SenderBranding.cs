using wa.application.Limits;

namespace wa.application.UseCases.Transfers;

/// <summary>F-PRF-001: the single sender-branding gate rule, shared by the recipient page (US-021-01) and the notification email (US-021-02).</summary>
public static class SenderBranding
{
    /// <summary>
    /// Org name to show, resolved from the sender's CURRENT profile and plan.
    /// Guest sends, blank org names and plans without Branding yield null.
    /// </summary>
    public static async Task<string?> ResolveOrgNameAsync(
        ISenderBrandingStore store, IFeaturesProvider features, Guid? ownerAppUserId, CancellationToken ct)
    {
        if (ownerAppUserId is null)
            return null;

        var row = await store.GetAsync(ownerAppUserId.Value, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.OrgName))
            return null;

        var resolved = await features.Resolve(row.PlanCode, ct);
        return resolved.Branding ? row.OrgName.Trim() : null;
    }

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
