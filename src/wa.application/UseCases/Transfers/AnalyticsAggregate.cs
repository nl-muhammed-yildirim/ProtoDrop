using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>Sparse SQL-grouped counts: per UTC day, per raw country value, per raw UA family value.</summary>
public sealed record AnalyticsAggregate(
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<(string? Key, int Count)> Countries,
    IReadOnlyList<(string? Key, int Count)> Browsers,
    IReadOnlyList<AnalyticsFile> FileItems,
    IReadOnlyList<FileCountRow> FileCounts);
