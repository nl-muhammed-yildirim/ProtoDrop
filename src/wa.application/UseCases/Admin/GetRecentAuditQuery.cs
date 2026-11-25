using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): GET /api/v1/admin/audit-recent — Recent actions list.
/// Both roles read (T-011-01 policy table: GET is a read → both roles). Read → NO audit row.
/// </summary>
public sealed record GetRecentAuditQuery : IRequest<IReadOnlyList<AuditLog>>;
