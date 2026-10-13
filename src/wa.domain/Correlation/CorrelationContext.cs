namespace wa.domain.Correlation;

/// <summary>Ambient per-request correlation state (TA-10.1). Set once by <c>CorrelationMiddleware</c> — the full W3C value is kept separately (App Insights) and the 8-hex correlationId is what error JSON and logs echo.</summary>
public static class CorrelationContext
{
    private static readonly AsyncLocal<TraceParent?> Current = new();

    /// <summary>The request's validated/generated traceparent; null outside a request or before the middleware runs.</summary>
    public static TraceParent? CurrentRequest => Current.Value;

    /// <summary>The 8-hex correlationId (TA-10.1), falling back to a freshly generated one when no context is active — never null in an error body.</summary>
    public static string CorrelationId => Current.Value?.CorrelationId ?? W3CTraceParser.NewTraceId()[..8];

    /// <summary>Called once per request by <c>CorrelationMiddleware</c>.</summary>
    public static void Set(TraceParent traceparent) => Current.Value = traceparent;
}
