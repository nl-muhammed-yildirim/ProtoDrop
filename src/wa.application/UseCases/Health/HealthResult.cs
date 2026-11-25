using Microsoft.Extensions.Logging;
using wa.application.Ports;

namespace wa.application.UseCases.Health;

/// <summary>The exact body of GET /health (AC-050-1). Property order is significant.</summary>
public sealed record HealthResult(string Status, string Db, string Sb);
