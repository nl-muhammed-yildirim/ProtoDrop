namespace wa.application.Ports;

/// <summary>Status of a single health-checked component.</summary>
public enum ComponentStatus
{
    /// <summary>The component was probed and answered.</summary>
    Ok,
    /// <summary>The component is not configured in this environment, so no probe was made.</summary>
    Skipped,
}
