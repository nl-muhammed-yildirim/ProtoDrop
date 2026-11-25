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
    /// <summary>Gets or sets the file ID of the finalize file.</summary>
    public Guid FileId { get; set; }
    /// <summary>Gets or sets the name of the finalize file.</summary>
    public string Name { get; set; } = null!;
    /// <summary>Gets or sets the size bytes of the finalize file.</summary>
    public long SizeBytes { get; set; }
    /// <summary>Gets or sets the content type of the finalize file.</summary>
    public string? ContentType { get; set; }
    /// <summary>Gets or sets the retries of the finalize file.</summary>
    public int Retries { get; set; }
}
