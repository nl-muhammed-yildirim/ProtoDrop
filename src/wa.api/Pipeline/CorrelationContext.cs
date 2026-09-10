namespace wa.api.Pipeline;

/// <summary>
/// Request-scoped correlation context (TA-10.1):
/// <see cref="CorrelationId"/> is the first 8 hex chars of the W3C trace-id,
/// available to the whole request pipeline (controller → handlers → logging).
/// The raw, possibly incoming, <c>traceparent</c> is available via
/// <see cref="HttpContextAccessor"/> if a deeper layer needs the full ID.
/// </summary>
public sealed class CorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public string TraceParent { get; private set; } = string.Empty;

    public void Populate(string traceParent)
    {
        TraceParent = traceParent;
        CorrelationId = traceParent.AsSpan(3, 8).ToString();
    }
}