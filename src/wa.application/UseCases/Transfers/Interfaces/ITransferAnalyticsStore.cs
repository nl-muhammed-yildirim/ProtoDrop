namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-023-01 (F-PRF-003): read port for the download analytics view. Implemented by a thin singleton adapter over WaDbContext.
/// All aggregation happens in SQL (EC-023-4); no raw DownloadEvent rows cross this port.
/// </summary>
public interface ITransferAnalyticsStore
{
    /// <summary>Owner-scoped, non-deleted transfer or null (foreign / guest / deleted / unknown are one case).</summary>
    /// <param name="ownerId">The signed-in user.</param>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The transfer facts, or <see langword="null"/> when it is not an existing transfer of this owner.</returns>
    Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct);

    /// <summary>Grouped counts of the transfer's DownloadEvents with CreatedAtUtc &gt;= sinceUtc (sequential queries on the singleton context).</summary>
    /// <param name="transferId">Transfer id.</param>
    /// <param name="sinceUtc">Only download events at or after this instant are counted.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>Sparse per-day, per-country, per-browser and per-file counts.</returns>
    Task<AnalyticsAggregate> AggregateAsync(Guid transferId, DateTime sinceUtc, CancellationToken ct);
}
