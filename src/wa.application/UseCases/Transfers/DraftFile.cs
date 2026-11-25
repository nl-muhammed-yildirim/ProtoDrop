using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>Per-file mint outcome: the browser uploads to <see cref="UploadUrl"/> under its own identity.</summary>
/// <param name="FileId">Identifier of the file of the draft file.</param>
/// <param name="UploadUrl">The upload URL of the draft file.</param>
public sealed record DraftFile(Guid FileId, Uri UploadUrl);
