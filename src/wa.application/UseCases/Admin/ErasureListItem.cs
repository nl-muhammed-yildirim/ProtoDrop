using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

/// <summary>Single erasure list item.</summary>
/// <param name="UserId">Identifier of the user of the erasure list item.</param>
/// <param name="Email">The email of the erasure list item.</param>
/// <param name="DeletedAtUtc">The deleted at UTC of the erasure list item.</param>
/// <param name="ReHomedTransferCount">The re homed transfer count of the erasure list item.</param>
/// <param name="SweepStatus">The sweep status of the erasure list item.</param>
/// <param name="SweptAtUtc">The swept at UTC of the erasure list item.</param>
public sealed record ErasureListItem(
    Guid UserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    string SweepStatus,
    DateTime? SweptAtUtc);
