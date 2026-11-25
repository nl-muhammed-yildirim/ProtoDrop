using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>US-047-01: architecture constants for the export feature (TA-6.3 style, not plan limits). D-22 retention default.</summary>
public static class DataExportPolicy
{
    /// <summary>How long a ready zip is kept before the f-data-export purge pass deletes it (D-22).</summary>
    public static readonly TimeSpan StoredRetention = TimeSpan.FromDays(7);

    /// <summary>Rows processed / purged per job pass.</summary>
    public const int BatchSize = 50;
}
