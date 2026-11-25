namespace wa.application.UseCases.Billing;

/// <summary>One paused subscription whose grace has ended and has not been claimed yet.</summary>
/// <param name="SubscriptionId">The paused subscription.</param>
/// <param name="GraceEndsAtUtc">UTC instant the grace ended; also the anchor of the job-lock claim.</param>
public sealed record GraceCandidate(Guid SubscriptionId, DateTime GraceEndsAtUtc);
