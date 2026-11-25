namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (FR-047-6): one erasure row for the admin report. Email is null when the AppUser row is gone.</summary>
public sealed record ErasureReportRow(
    Guid AppUserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    DateTime? SweptAtUtc);
