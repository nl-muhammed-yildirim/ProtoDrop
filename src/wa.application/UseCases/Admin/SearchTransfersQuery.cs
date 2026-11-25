using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): GET /api/v1/admin/transfers — platform-wide admin transfer search, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim/null, status vocabulary resolution, range validation, cursor decode, then one store call.
/// </summary>
/// <param name="Q">The q of the search transfers query.</param>
/// <param name="Status">The status of the search transfers query.</param>
/// <param name="MinBytes">The min bytes of the search transfers query.</param>
/// <param name="MaxBytes">The max bytes of the search transfers query.</param>
/// <param name="FromUtc">The from UTC of the search transfers query.</param>
/// <param name="ToUtc">The to UTC of the search transfers query.</param>
/// <param name="Cursor">The cursor of the search transfers query.</param>
/// <param name="Limit">The limit of the search transfers query.</param>
public sealed record SearchTransfersQuery(
    string? Q,
    string? Status,
    long? MinBytes,
    long? MaxBytes,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Cursor,
    int Limit) : IRequest<SearchTransfersResult>;
