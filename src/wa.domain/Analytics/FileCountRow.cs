namespace wa.domain.Analytics;

/// <summary>SQL-grouped per-file row: FileId null = download-all (zip); LastAtUtc = most recent event (tie-break).</summary>
/// <param name="FileId">Identifier of the file of the file count row.</param>
/// <param name="Count">The count of the file count row.</param>
/// <param name="LastAtUtc">The last at UTC of the file count row.</param>
public sealed record FileCountRow(Guid? FileId, int Count, DateTime LastAtUtc);
