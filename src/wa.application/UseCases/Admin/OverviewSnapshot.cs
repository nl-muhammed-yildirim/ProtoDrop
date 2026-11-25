namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the cached snapshot — data + the timestamp fixed at computation time (AC-011-1 same-timestamp half).</summary>
/// <param name="Data">The computed overview aggregates.</param>
/// <param name="SnapshotAtUtc">UTC instant the aggregates were computed.</param>
public record OverviewSnapshot(OverviewData Data, DateTime SnapshotAtUtc);
