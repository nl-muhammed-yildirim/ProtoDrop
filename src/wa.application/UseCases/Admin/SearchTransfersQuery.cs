using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): GET /api/v1/admin/transfers — platform-wide admin transfer search, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim/null, status vocabulary resolution, range validation, cursor decode, then one store call.
/// </summary>
public sealed record SearchTransfersQuery(
    string? Q,
    string? Status,
    long? MinBytes,
    long? MaxBytes,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Cursor,
    int Limit) : IRequest<SearchTransfersResult>;
