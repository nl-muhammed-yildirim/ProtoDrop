namespace wa.application.UseCases.Admin;

/// <summary>One row of the admin erasure report as returned by the API, with the email masked unless the caller may see it.</summary>
/// <param name="UserId">Id of the deleted account.</param>
/// <param name="Email">Account email: full for SuperAdmin, masked for Operator, null when the user row no longer exists; PII (TA-9.4).</param>
/// <param name="DeletedAtUtc">UTC instant the account was deleted.</param>
/// <param name="ReHomedTransferCount">Number of transfers that were re-homed to anonymous at deletion.</param>
/// <param name="SweepStatus">Either <c>pending</c> or <c>swept</c> (see <see cref="ErasureReportPolicy"/>).</param>
/// <param name="SweptAtUtc">UTC instant the GDPR sweep finished; null while pending.</param>
public sealed record ErasureListItem(
    Guid UserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    string SweepStatus,
    DateTime? SweptAtUtc);
