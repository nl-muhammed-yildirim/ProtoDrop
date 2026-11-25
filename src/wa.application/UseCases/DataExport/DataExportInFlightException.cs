using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.DataExport;

/// <summary>EC-047-6: an export is already processing for this user — endpoint maps to 409 EXPORT_IN_FLIGHT.</summary>
public sealed class DataExportInFlightException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DataExportInFlightException"/> class with the standard message.</summary>
    public DataExportInFlightException() : base("An export is already in progress.") { }
}
