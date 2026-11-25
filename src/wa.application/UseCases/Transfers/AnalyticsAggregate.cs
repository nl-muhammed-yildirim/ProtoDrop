using wa.domain.Analytics;

namespace wa.application.UseCases.Transfers;

/// <summary>Sparse SQL-grouped counts: per UTC day, per raw country value, per raw UA family value.</summary>
/// <param name="Daily">The daily of the analytics aggregate.</param>
/// <param name="Countries">The countries of the analytics aggregate.</param>
/// <param name="Browsers">The browsers of the analytics aggregate.</param>
/// <param name="FileItems">The file items of the analytics aggregate.</param>
/// <param name="FileCounts">The file counts of the analytics aggregate.</param>
public sealed record AnalyticsAggregate(
    IReadOnlyList<DailyCount> Daily,
    IReadOnlyList<(string? Key, int Count)> Countries,
    IReadOnlyList<(string? Key, int Count)> Browsers,
    IReadOnlyList<AnalyticsFile> FileItems,
    IReadOnlyList<FileCountRow> FileCounts);
