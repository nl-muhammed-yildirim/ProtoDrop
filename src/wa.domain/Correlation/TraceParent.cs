namespace wa.domain.Correlation;

/// <summary>A validated W3C traceparent (version 00).</summary>
/// <param name="Version">The version of the trace parent.</param>
/// <param name="TraceId">Identifier of the trace of the trace parent.</param>
/// <param name="ParentId">Identifier of the parent of the trace parent.</param>
/// <param name="Flags">The flags of the trace parent.</param>
public sealed record TraceParent(string Version, string TraceId, string ParentId, string Flags)
{
    /// <summary>The full header value — kept separately for App Insights (TA-10.1).</summary>
    /// <returns>The resulting text.</returns>
    public string ToHeader() => $"{Version}-{TraceId}-{ParentId}-{Flags}";

    /// <summary>CorrelationId = first 8 hex of trace-id (TA-10.1) — exactly 8 chars, not the full trace-id.</summary>
    public string CorrelationId => TraceId[..8];
}
