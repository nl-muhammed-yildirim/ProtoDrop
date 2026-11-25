using MediatR;
using wa.domain;

namespace wa.application.UseCases.Admin;

/// <summary>T-011-09 (TA-4.2#25): last 50 rows, newest first.</summary>
public sealed class GetRecentAuditQueryHandler : IRequestHandler<GetRecentAuditQuery, IReadOnlyList<AuditLog>>
{
    private readonly IAuditReadStore _store;

    public GetRecentAuditQueryHandler(IAuditReadStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<AuditLog>> Handle(GetRecentAuditQuery request, CancellationToken cancellationToken)
    {
        // 50 = US-011-04 happy path 5's number (last 50 actions) — pinned as a literal, not a config value.
        return (await _store.GetRecentAsync(50, cancellationToken)).ToList();
    }
}
