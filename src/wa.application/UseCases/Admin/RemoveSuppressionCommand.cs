using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#24 / FR-011-5): DELETE /api/v1/admin/suppressions/{id:guid} — SuperAdmin-only write.
/// Returns the REMOVED row or null (unknown id → endpoint maps to 404 NOT_FOUND via BusinessException).
/// </summary>
/// <param name="Id">Primary key of the suppression row to remove.</param>
public sealed record RemoveSuppressionCommand(Guid Id) : IRequest<EmailSuppression?>;
