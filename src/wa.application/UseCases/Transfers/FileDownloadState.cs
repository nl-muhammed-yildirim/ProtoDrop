using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>The download endpoint's state mapping (TA-4.2#6): terminal states + the auth gate + mint success.</summary>
public enum FileDownloadState
{
    /// <summary>Ok.</summary>
    Ok,
    /// <summary>Expired.</summary>
    Expired,
    /// <summary>Download limit.</summary>
    DownloadLimit,
    /// <summary>Not found.</summary>
    NotFound,
    /// <summary>Unauthenticated.</summary>
    Unauthenticated
}
