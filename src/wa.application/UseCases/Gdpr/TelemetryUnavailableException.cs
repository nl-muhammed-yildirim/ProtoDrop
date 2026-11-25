namespace wa.application.UseCases.Gdpr;

/// <summary>The telemetry store could not be reached; the sweep leaves the marker pending and retries next night (EC-047-4).</summary>
public sealed class TelemetryUnavailableException : Exception
{
    public TelemetryUnavailableException() { }
    public TelemetryUnavailableException(string message) : base(message) { }
    public TelemetryUnavailableException(string message, Exception inner) : base(message, inner) { }
}
