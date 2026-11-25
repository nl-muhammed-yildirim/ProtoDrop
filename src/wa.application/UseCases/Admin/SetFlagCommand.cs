using MediatR;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 / US-044-01 (TA-4.2#23, FR-044-5): PUT /api/v1/admin/flags/{key} — SuperAdmin-only flag value write.
/// The value takes effect within 30 s via the EXISTING FlagsCache TTL (TA-3.4) — no invalidation code here.
/// </summary>
/// <param name="Key">Flag key from the route; must be a seeded key.</param>
/// <param name="Value">New raw value from the request body; trimmed and validated against the key's type.</param>
public sealed record SetFlagCommand(string Key, string Value) : IRequest<SetFlagResult>;
