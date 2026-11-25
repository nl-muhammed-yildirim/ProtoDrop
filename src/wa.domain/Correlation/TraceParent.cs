namespace wa.domain.Correlation;

/// <summary>A validated W3C traceparent (version 00).</summary>
/// <param name="Version">Two-hex version field; only <c>00</c> is accepted.</param>
/// <param name="TraceId">32 lowercase hex characters identifying the whole distributed trace.</param>
/// <param name="ParentId">16 lowercase hex characters identifying the calling span.</param>
/// <param name="Flags">Two-hex trace-flags byte (for example sampled).</param>
public sealed record TraceParent(string Version, string TraceId, string ParentId, string Flags)
{
    /// <summary>The full header value — kept separately for App Insights (TA-10.1).</summary>
    /// <returns>The four fields joined with dashes, ready to send as a <c>traceparent</c> header.</returns>
    public string ToHeader() => $"{Version}-{TraceId}-{ParentId}-{Flags}";

    /// <summary>CorrelationId = first 8 hex of trace-id (TA-10.1) — exactly 8 chars, not the full trace-id.</summary>
    public string CorrelationId => TraceId[..8];
}
