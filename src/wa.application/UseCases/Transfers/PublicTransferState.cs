using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>The recipient page's state mapping (TA-4.2#4). NotFound covers missing rows + malformed linkIds + Status=4 Deleted — deliberately indistinguishable (FR-003-9).</summary>
public enum PublicTransferState { Active, Expired, DownloadLimit, NotFound }
