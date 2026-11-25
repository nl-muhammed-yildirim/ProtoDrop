using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>Per-file mint outcome: the browser uploads to <see cref="UploadUrl"/> under its own identity.</summary>
/// <param name="FileId">Server-assigned id of the staged file.</param>
/// <param name="UploadUrl">Write SAS URL the browser uploads the file to.</param>
public sealed record DraftFile(Guid FileId, Uri UploadUrl);
