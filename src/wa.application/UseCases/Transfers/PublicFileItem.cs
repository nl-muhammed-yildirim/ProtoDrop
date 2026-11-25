using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>A file row of the active transfer — plain projection, no EF entity (golden rule 8: no DbContext in use cases).</summary>
/// <param name="FileId">Identifier of the file of the public file item.</param>
/// <param name="Name">The name of the public file item.</param>
/// <param name="SizeBytes">The size bytes of the public file item.</param>
public sealed record PublicFileItem(Guid FileId, string Name, long SizeBytes);
