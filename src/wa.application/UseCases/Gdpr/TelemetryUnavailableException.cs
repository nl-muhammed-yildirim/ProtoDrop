namespace wa.application.UseCases.Gdpr;

/// <summary>The telemetry store could not be reached; the sweep leaves the marker pending and retries next night (EC-047-4).</summary>
public sealed class TelemetryUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class without a message.</summary>
    public TelemetryUnavailableException() { }
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class with a message.</summary>
    /// <param name="message">Description of why the telemetry store is unreachable.</param>
    public TelemetryUnavailableException(string message) : base(message) { }
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class with a message and the underlying failure.</summary>
    /// <param name="message">Description of why the telemetry store is unreachable.</param>
    /// <param name="inner">The transport or service exception that caused it.</param>
    public TelemetryUnavailableException(string message, Exception inner) : base(message, inner) { }
}
