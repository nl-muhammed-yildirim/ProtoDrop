using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>A file row of the active transfer — plain projection, no EF entity (golden rule 8: no DbContext in use cases).</summary>
public sealed record PublicFileItem(Guid FileId, string Name, long SizeBytes);
