namespace wa.application.UseCases.Gdpr;

/// <summary>The telemetry store could not be reached; the sweep leaves the marker pending and retries next night (EC-047-4).</summary>
public sealed class TelemetryUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class.</summary>
    public TelemetryUnavailableException() { }
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class.</summary>
    /// <param name="message">The message.</param>
    public TelemetryUnavailableException(string message) : base(message) { }
    /// <summary>Initializes a new instance of the <see cref="TelemetryUnavailableException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The inner.</param>
    public TelemetryUnavailableException(string message, Exception inner) : base(message, inner) { }
}
