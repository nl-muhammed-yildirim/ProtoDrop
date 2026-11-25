using MediatR;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-04 (TA-4.2#21 / FR-011-3): GET /api/v1/admin/transfers — platform-wide admin transfer search, cursor-paged at 25 rows.
/// Handler logic is pure (no EF): q trim/null, status vocabulary resolution, range validation, cursor decode, then one store call.
/// </summary>
/// <param name="Q">Search text matched against the link id and recipient addresses (and file names and sender with feature.search on).</param>
/// <param name="Status">Comma-separated status names to include; null for all statuses.</param>
/// <param name="MinBytes">Inclusive lower bound on total size in bytes; null for none.</param>
/// <param name="MaxBytes">Inclusive upper bound on total size in bytes; null for none.</param>
/// <param name="FromUtc">Inclusive lower bound on creation time; null for none.</param>
/// <param name="ToUtc">Inclusive upper bound on creation time; null for none.</param>
/// <param name="Cursor">Opaque cursor from the previous page; null or empty for the first page.</param>
/// <param name="Limit">Maximum number of rows to return.</param>
public sealed record SearchTransfersQuery(
    string? Q,
    string? Status,
    long? MinBytes,
    long? MaxBytes,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Cursor,
    int Limit) : IRequest<SearchTransfersResult>;
