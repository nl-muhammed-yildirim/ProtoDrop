namespace wa.application.UseCases.Transfers;

/// <summary>T-009-04 (TA-5.3 / FR-009-3): the store's delete result — the MediatR contract type for this use case.</summary>
/// <param name="DeletedAtUtc">UTC instant stored as the deletion time; used as the event time.</param>
/// <param name="LinkId">Public link id of the deleted transfer, used as the event partition key.</param>
public sealed record MarkDeleteResult(DateTime DeletedAtUtc, string LinkId);
