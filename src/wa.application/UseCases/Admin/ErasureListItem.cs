using wa.domain.Privacy;
using wa.domain.Transfers;

namespace wa.application.UseCases.Admin;

public sealed record ErasureListItem(
    Guid UserId,
    string? Email,
    DateTime DeletedAtUtc,
    int ReHomedTransferCount,
    string SweepStatus,
    DateTime? SweptAtUtc);
