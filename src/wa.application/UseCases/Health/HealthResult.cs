using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>The exact body of GET /health (AC-050-1). Property order is significant.</summary>
/// <param name="Status">The status of the health result.</param>
/// <param name="Db">The database of the health result.</param>
/// <param name="Sb">The Service Bus of the health result.</param>
public sealed record HealthResult(string Status, string Db, string Sb);
