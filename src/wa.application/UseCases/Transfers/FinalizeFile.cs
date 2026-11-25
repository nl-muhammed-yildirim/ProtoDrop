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

/// <summary>One file in the finalize request (TA-4.2#2 request row). POCO with settable properties for minimal-API binding.</summary>
public sealed class FinalizeFile
{
    public Guid FileId { get; set; }
    public string Name { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string? ContentType { get; set; }
    public int Retries { get; set; }
}
