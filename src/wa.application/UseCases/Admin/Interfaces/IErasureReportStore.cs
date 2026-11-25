namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (TA-2.3): read port for the erasure report — cursor-paged, newest deletion first.</summary>
public interface IErasureReportStore
{
    Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}
