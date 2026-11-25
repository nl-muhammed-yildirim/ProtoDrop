using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>A file in the finalized transfer result.</summary>
/// <param name="FileId">Identifier of the file of the finalized file.</param>
/// <param name="Name">The name of the finalized file.</param>
/// <param name="SizeBytes">The size bytes of the finalized file.</param>
/// <param name="ContentType">The content type of the finalized file.</param>
public sealed record FinalizedFile(Guid FileId, string Name, long SizeBytes, string? ContentType);
