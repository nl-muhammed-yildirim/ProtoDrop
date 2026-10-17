using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-014 (TA-4.2#4): the read surface for GET /api/v1/public/transfers/{linkId}.
/// Implemented by a thin singleton adapter over the adapter-owned WaDbContext (same style as ITransferStore).
/// </summary>
public interface IPublicTransferStore
{
    /// <summary>Return (transfer, files ordered by SortOrder) or (null, empty) when no row matches.</summary>
    Task<(Transfer? Transfer, IReadOnlyList<PublicFileItem> Files)> GetByLinkIdAsync(string linkId, CancellationToken ct);
}
