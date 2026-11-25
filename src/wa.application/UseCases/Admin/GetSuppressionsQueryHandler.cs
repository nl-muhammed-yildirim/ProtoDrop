using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-09 (TA-4.2#24 / FR-011-2): list all suppression rows ordered CreatedAtUtc descending.</summary>
public sealed class GetSuppressionsQueryHandler : IRequestHandler<GetSuppressionsQuery, IReadOnlyList<EmailSuppression>>
{
    private readonly IAdminSuppressionStore _store;

    public GetSuppressionsQueryHandler(IAdminSuppressionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<EmailSuppression>> Handle(GetSuppressionsQuery request, CancellationToken cancellationToken)
    {
        // Newest first — deterministic (unit-testable without EF).
        return (await _store.ListAsync(cancellationToken)).OrderByDescending(s => s.CreatedAtUtc).ToList();
    }
}
