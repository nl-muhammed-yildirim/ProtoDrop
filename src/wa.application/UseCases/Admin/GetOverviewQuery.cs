using MediatR;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-03 (TA-4.2#20 / FR-011-6): GET /api/v1/admin/overview — four aggregates + recent transfers list, cached 5 min.
/// Email counts are the documented approximation (US-011-01 Technical notes): sent = count of EmailRecipient.NotifiedAtUtc in the last 7 days; failed = count of email.failed outbox rows in the last 7 days.
/// </summary>
public sealed record GetOverviewQuery : IRequest<GetOverviewResult>;
