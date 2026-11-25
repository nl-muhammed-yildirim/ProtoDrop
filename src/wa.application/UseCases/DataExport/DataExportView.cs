using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>Wire view of the user's latest export; <paramref name="DownloadUrl"/> is set only when the state is <c>ready</c>.</summary>
/// <param name="State">Wire name of the state: <c>processing</c>, <c>ready</c> or <c>failed</c>.</param>
/// <param name="CreatedAtUtc">UTC instant the export was requested.</param>
/// <param name="ReadyAtUtc">UTC instant the zip became available; null until then.</param>
/// <param name="DownloadUrl">Fresh 24-hour download URL; null unless the export is ready.</param>
public sealed record DataExportView(string State, DateTime CreatedAtUtc, DateTime? ReadyAtUtc, Uri? DownloadUrl);
