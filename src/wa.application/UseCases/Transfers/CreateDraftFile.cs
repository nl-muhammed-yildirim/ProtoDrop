using wa.application.Limits;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Transfers;

/// <summary>One staged file of a draft (TA-4.2#1 request row). POCO with settable properties for minimal-API binding — camelCase JSON binds to PascalCase via the default binder.</summary>
public sealed class CreateDraftFile
{
    /// <summary>Gets or sets the name of the create draft file.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Size in bytes (TA-4.2#1: <c>sizeBytes</c>).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the content type of the create draft file.</summary>
    public string? ContentType { get; set; }
}
