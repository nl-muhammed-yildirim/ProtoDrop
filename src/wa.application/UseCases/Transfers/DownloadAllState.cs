using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using wa.application.Blob;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Correlation;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>The download-all endpoint's state mapping (TA-4.2#7): terminal states + the auth gate + zip ready/generating.</summary>
public enum DownloadAllState { Ok, Generating, Expired, DownloadLimit, NotFound, Unauthenticated, StorageUnavailable }
