using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-09 (TA-4.2#25): last 50 rows, newest first.</summary>
public sealed class GetRecentAuditQueryHandler : IRequestHandler<GetRecentAuditQuery, IReadOnlyList<AuditLog>>
{
    private readonly IAuditReadStore _store;

    /// <summary>Initializes a new instance of the <see cref="GetRecentAuditQueryHandler"/> class.</summary>
    /// <param name="store">Read store for audit rows.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
    public GetRecentAuditQueryHandler(IAuditReadStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> Handle(GetRecentAuditQuery request, CancellationToken cancellationToken)
    {
        // 50 = US-011-04 happy path 5's number (last 50 actions) — pinned as a literal, not a config value.
        return (await _store.GetRecentAsync(50, cancellationToken)).ToList();
    }
}
