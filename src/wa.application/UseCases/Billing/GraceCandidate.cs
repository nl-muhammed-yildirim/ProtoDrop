namespace wa.application.UseCases.Billing;

/// <summary>One paused subscription whose grace has ended and has not been claimed yet.</summary>
/// <param name="SubscriptionId">Identifier of the subscription of the grace candidate.</param>
/// <param name="GraceEndsAtUtc">The grace ends at UTC of the grace candidate.</param>
public sealed record GraceCandidate(Guid SubscriptionId, DateTime GraceEndsAtUtc);
