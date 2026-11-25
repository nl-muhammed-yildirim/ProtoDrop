namespace wa.domain;

/// <summary>US-047-01: the three <see cref="DataExport.State"/> values and their wire names.</summary>
public static class DataExportState
{
    public const byte Processing = 0;
    public const byte Ready = 1;
    public const byte Failed = 2;

    /// <summary>The lowercase wire name of a state; throws for an unknown value.</summary>
    public static string WireName(byte state) => state switch
    {
        Processing => "processing",
        Ready => "ready",
        Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown data export state."),
    };
}
