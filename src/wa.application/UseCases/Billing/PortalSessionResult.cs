namespace wa.application.UseCases.Billing;

/// <summary>Url null means no Stripe customer on file — the UI falls back to Upgrade.</summary>
/// <param name="Url">Billing Portal URL, or null when the user has no Stripe customer on file.</param>
public sealed record PortalSessionResult(string? Url);
