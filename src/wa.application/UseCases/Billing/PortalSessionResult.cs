using Microsoft.Extensions.Configuration;

namespace wa.application.UseCases.Billing;

/// <summary>Url null means no Stripe customer on file — the UI falls back to Upgrade.</summary>
/// <param name="Url">The URL of the portal session result.</param>
public sealed record PortalSessionResult(string? Url);
