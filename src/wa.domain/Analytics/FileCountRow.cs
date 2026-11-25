namespace wa.domain.Analytics;

/// <summary>SQL-grouped per-file row: FileId null = download-all (zip); LastAtUtc = most recent event (tie-break).</summary>
public sealed record FileCountRow(Guid? FileId, int Count, DateTime LastAtUtc);
