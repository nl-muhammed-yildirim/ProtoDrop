namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (FR-047-6): one erasure row for the admin report. Email is null when the AppUser row is gone.</summary>
public sealed record ErasureReportRow(
    Guid AppUserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    DateTime? SweptAtUtc);

/// <summary>US-047-03 (TA-2.3): read port for the erasure report — cursor-paged, newest deletion first.</summary>
public interface IErasureReportStore
{
    Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
        DateTime? afterUtc,
        Guid? afterIdExcluded,
        int limit,
        CancellationToken ct);
}
