namespace wa.application.UseCases.Billing;

/// <summary>One paused subscription whose grace has ended and has not been claimed yet.</summary>
public sealed record GraceCandidate(Guid SubscriptionId, DateTime GraceEndsAtUtc);
