namespace wa.domain;

/// <summary>US-047-01: the three <see cref="DataExport.State"/> values and their wire names.</summary>
public static class DataExportState
{
    /// <summary>The processing constant; value <c>0</c>.</summary>
    public const byte Processing = 0;
    /// <summary>The ready constant; value <c>1</c>.</summary>
    public const byte Ready = 1;
    /// <summary>The failed constant; value <c>2</c>.</summary>
    public const byte Failed = 2;

    /// <summary>The lowercase wire name of a state; throws for an unknown value.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The resulting text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when an argument is outside the allowed range.</exception>
    public static string WireName(byte state) => state switch
    {
        Processing => "processing",
        Ready => "ready",
        Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown data export state."),
    };
}
