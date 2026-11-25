namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (FR-047-6): one erasure row for the admin report. Email is null when the AppUser row is gone.</summary>
/// <param name="AppUserId">Identifier of the app user of the erasure report row.</param>
/// <param name="Email">The email of the erasure report row.</param>
/// <param name="DeletedAtUtc">The deleted at UTC of the erasure report row.</param>
/// <param name="ReHomedTransferCount">The re homed transfer count of the erasure report row.</param>
/// <param name="SweptAtUtc">The swept at UTC of the erasure report row.</param>
public sealed record ErasureReportRow(
    Guid AppUserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    DateTime? SweptAtUtc);
