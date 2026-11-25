using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01: wire body bound by Minimal API. The session supplies UserId — not in the body.</summary>
public sealed record CheckoutRequest(string? PlanCode, string? Interval);
