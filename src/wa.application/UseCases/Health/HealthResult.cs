namespace wa.application.UseCases.Health;

/// <summary>The exact body of GET /health (AC-050-1). Property order is significant.</summary>
/// <param name="Status">Overall status, <c>ok</c> when every probed component is healthy.</param>
/// <param name="Db">Database status, <c>ok</c>.</param>
/// <param name="Sb">Service Bus status: <c>ok</c> or <c>skipped</c> when Service Bus is not configured.</param>
public sealed record HealthResult(string Status, string Db, string Sb);
