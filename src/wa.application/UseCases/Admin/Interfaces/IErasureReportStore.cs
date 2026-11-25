namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (TA-2.3): read port for the erasure report — cursor-paged, newest deletion first.</summary>
public interface IErasureReportStore
{
    /// <summary>Gets the page.</summary>
    /// <param name="afterUtc">The after UTC.</param>
    /// <param name="afterIdExcluded">The after ID excluded.</param>
    /// <param name="limit">The limit.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>A tuple holding the items and next cursor values.</returns>
    Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}
