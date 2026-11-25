namespace wa.application.UseCases.Admin;

/// <summary>T-011-03: the cached snapshot — data + the timestamp fixed at computation time (AC-011-1 same-timestamp half).</summary>
/// <param name="Data">The data of the overview snapshot.</param>
/// <param name="SnapshotAtUtc">The snapshot at UTC of the overview snapshot.</param>
public record OverviewSnapshot(OverviewData Data, DateTime SnapshotAtUtc);
