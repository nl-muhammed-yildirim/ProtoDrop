namespace wa.application.UseCases.Admin;

/// <summary>US-047-03 (FR-047-6): one erasure row for the admin report. Email is null when the AppUser row is gone.</summary>
/// <param name="AppUserId">Id of the deleted account.</param>
/// <param name="Email">Account email, or null when the AppUser row is gone; PII (TA-9.4).</param>
/// <param name="DeletedAtUtc">UTC instant the account was deleted.</param>
/// <param name="ReHomedTransferCount">Number of transfers re-homed to anonymous at deletion.</param>
/// <param name="SweptAtUtc">UTC instant the GDPR sweep finished; null while pending.</param>
public sealed record ErasureReportRow(
    Guid AppUserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    DateTime? SweptAtUtc);
