using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01: wire body bound by Minimal API. The session supplies UserId — not in the body.</summary>
/// <param name="PlanCode">Code of the paid plan to subscribe to; null or unknown values are rejected.</param>
/// <param name="Interval">Billing interval name, <c>monthly</c> or <c>annual</c>.</param>
public sealed record CheckoutRequest(string? PlanCode, string? Interval);
