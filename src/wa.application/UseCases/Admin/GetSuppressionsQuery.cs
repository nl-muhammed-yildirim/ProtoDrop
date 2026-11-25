using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#24 / FR-011-2): GET /api/v1/admin/suppressions — the Suppressions screen list.
/// Both roles read (T-011-01 policy table: GET is a read → both roles). Read → NO audit row.
/// </summary>
public sealed record GetSuppressionsQuery : IRequest<IReadOnlyList<EmailSuppression>>;
