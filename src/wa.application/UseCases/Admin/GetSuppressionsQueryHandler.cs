using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-09 (TA-4.2#24 / FR-011-2): list all suppression rows ordered CreatedAtUtc descending.</summary>
public sealed class GetSuppressionsQueryHandler : IRequestHandler<GetSuppressionsQuery, IReadOnlyList<EmailSuppression>>
{
    private readonly IAdminSuppressionStore _store;

    /// <summary>Initializes a new instance of the <see cref="GetSuppressionsQueryHandler"/> class.</summary>
    /// <param name="store">Admin store that lists email suppressions.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
    public GetSuppressionsQueryHandler(IAdminSuppressionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailSuppression>> Handle(GetSuppressionsQuery request, CancellationToken cancellationToken)
    {
        // Newest first — deterministic (unit-testable without EF).
        return (await _store.ListAsync(cancellationToken)).OrderByDescending(s => s.CreatedAtUtc).ToList();
    }
}
