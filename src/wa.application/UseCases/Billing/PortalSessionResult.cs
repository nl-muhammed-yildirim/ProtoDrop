using Microsoft.Extensions.Configuration;

namespace wa.application.UseCases.Billing;

/// <summary>Url null means no Stripe customer on file — the UI falls back to Upgrade.</summary>
public sealed record PortalSessionResult(string? Url);
