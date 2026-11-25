namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the cached snapshot — data + the timestamp fixed at computation time (AC-011-1 same-timestamp half).</summary>
public record OverviewSnapshot(OverviewData Data, DateTime SnapshotAtUtc);
