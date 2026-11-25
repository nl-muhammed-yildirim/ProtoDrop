using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>The unlock endpoint's state mapping — mirrors endpoint 4's PublicTransferState terminal states (TA-9.6 enumeration: same bodies) + WrongPassword for the 401.</summary>
public enum UnlockState { Active, Expired, DownloadLimit, NotFound, WrongPassword }
