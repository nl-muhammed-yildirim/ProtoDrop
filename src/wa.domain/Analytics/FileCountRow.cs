namespace wa.domain.Analytics;

/// <summary>SQL-grouped per-file row: FileId null = download-all (zip); LastAtUtc = most recent event (tie-break).</summary>
/// <param name="FileId">File the downloads belong to; null for download-all (zip) events.</param>
/// <param name="Count">Number of download events for that file.</param>
/// <param name="LastAtUtc">UTC time of the most recent event; breaks ties between equal counts.</param>
public sealed record FileCountRow(Guid? FileId, int Count, DateTime LastAtUtc);
