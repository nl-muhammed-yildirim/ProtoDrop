using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>Sparse SQL-grouped counts: per UTC day, per raw country value, per raw UA family value.</summary>
/// <param name="Daily">Download counts per UTC day; days without events are absent.</param>
/// <param name="Countries">Download counts per raw country value; keys may be null or malformed.</param>
/// <param name="Browsers">Download counts per raw browser-family value; keys may be null.</param>
/// <param name="FileItems">The transfer's files, so zero-download files can be listed.</param>
/// <param name="FileCounts">Per-file download counts and last-event times.</param>
public sealed record AnalyticsAggregate(
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<(string? Key, int Count)> Countries,
    IReadOnlyList<(string? Key, int Count)> Browsers,
    IReadOnlyList<AnalyticsFile> FileItems,
    IReadOnlyList<FileCountRow> FileCounts);
