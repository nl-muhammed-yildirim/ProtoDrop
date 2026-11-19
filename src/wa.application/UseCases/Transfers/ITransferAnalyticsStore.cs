using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// US-023-01 (F-PRF-003): read port for the download analytics view. Implemented by a thin singleton adapter over WaDbContext.
/// All aggregation happens in SQL (EC-023-4); no raw DownloadEvent rows cross this port.
/// </summary>
public interface ITransferAnalyticsStore
{
    /// <summary>Owner-scoped, non-deleted transfer or null (foreign / guest / deleted / unknown are one case).</summary>
    Task<AnalyticsTransfer?> GetOwnedTransferAsync(Guid ownerId, Guid transferId, CancellationToken ct);

    /// <summary>Grouped counts of the transfer's DownloadEvents with CreatedAtUtc &gt;= sinceUtc (sequential queries on the singleton context).</summary>
    Task<AnalyticsAggregate> AggregateAsync(Guid transferId, DateTime sinceUtc, CancellationToken ct);
}

/// <summary>The owner-scoped transfer facts the handler needs (window start + status).</summary>
public sealed record AnalyticsTransfer(Guid Id, DateTime CreatedAtUtc, byte Status);

/// <summary>Sparse SQL-grouped counts: per UTC day, per raw country value, per raw UA family value.</summary>
public sealed record AnalyticsAggregate(
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<(string? Key, int Count)> Countries,
    IReadOnlyList<(string? Key, int Count)> Browsers);
