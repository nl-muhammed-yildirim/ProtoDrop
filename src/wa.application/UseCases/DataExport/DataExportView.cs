using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>Wire view: DownloadUrl is set only when State is "ready".</summary>
/// <param name="State">The state of the data export view.</param>
/// <param name="CreatedAtUtc">The created at UTC of the data export view.</param>
/// <param name="ReadyAtUtc">The ready at UTC of the data export view.</param>
/// <param name="DownloadUrl">The download URL of the data export view.</param>
public sealed record DataExportView(string State, DateTime CreatedAtUtc, DateTime? ReadyAtUtc, Uri? DownloadUrl);
