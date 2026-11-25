namespace wa.domain;

/// <summary>US-047-01: the three <see cref="DataExport.State"/> values and their wire names.</summary>
public static class DataExportState
{
    /// <summary>The export zip is still being built by the worker (stored as 0).</summary>
    public const byte Processing = 0;
    /// <summary>The zip is built and downloadable (stored as 1).</summary>
    public const byte Ready = 1;
    /// <summary>The worker gave up building the zip (stored as 2); the user may request a new export.</summary>
    public const byte Failed = 2;

    /// <summary>The lowercase wire name of a state; throws for an unknown value.</summary>
    /// <param name="state">Stored state byte of a <see cref="DataExport"/>.</param>
    /// <returns>The lowercase API name: <c>processing</c>, <c>ready</c> or <c>failed</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="state"/> is not one of the three known values.</exception>
    public static string WireName(byte state) => state switch
    {
        Processing => "processing",
        Ready => "ready",
        Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown data export state."),
    };
}
