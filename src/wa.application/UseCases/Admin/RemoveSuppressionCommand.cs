using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-09 (TA-4.2#24 / FR-011-5): DELETE /api/v1/admin/suppressions/{id:guid} — SuperAdmin-only write.
/// Returns the REMOVED row or null (unknown id → endpoint maps to 404 NOT_FOUND via BusinessException).
/// </summary>
public sealed record RemoveSuppressionCommand(Guid Id) : IRequest<EmailSuppression?>;

/// <summary>T-011-09 (TA-4.2#24): remove a suppression row — null pass-through (endpoint maps to 404).</summary>
public sealed class RemoveSuppressionCommandHandler : IRequestHandler<RemoveSuppressionCommand, EmailSuppression?>
{
    private readonly IAdminSuppressionStore _store;

    public RemoveSuppressionCommandHandler(IAdminSuppressionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<EmailSuppression?> Handle(RemoveSuppressionCommand request, CancellationToken cancellationToken)
    {
        // Null = unknown id (endpoint maps to 404 NOT_FOUND — mirrors T-011-05/07). No exception type needed (single case).
        return await _store.RemoveByIdAsync(request.Id, cancellationToken);
    }
}
