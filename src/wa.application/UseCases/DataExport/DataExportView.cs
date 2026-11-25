using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>Wire view: DownloadUrl is set only when State is "ready".</summary>
public sealed record DataExportView(string State, DateTime CreatedAtUtc, DateTime? ReadyAtUtc, Uri? DownloadUrl);
