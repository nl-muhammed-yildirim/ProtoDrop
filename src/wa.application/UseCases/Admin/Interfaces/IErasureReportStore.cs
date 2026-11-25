namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (TA-2.3): read port for the erasure report — cursor-paged, newest deletion first.</summary>
public interface IErasureReportStore
{
    /// <summary>Reads one page of the erasure report, newest deletion first.</summary>
    /// <param name="afterUtc">Cursor: only rows deleted before this instant (null for the first page).</param>
    /// <param name="afterIdExcluded">Cursor tie-break: with an equal timestamp, only rows with a smaller user id (null for none).</param>
    /// <param name="limit">Page size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>The rows and the cursor of the next page (null when this is the last page).</returns>
    Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}
