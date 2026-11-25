using Microsoft.Extensions.Logging;
using wa.application.Telemetry;
using wa.domain.Telemetry;
using wa.domain.ValueObjects;

namespace wa.application.UseCases.Transfers;

/// <summary>The unlock endpoint's state mapping — mirrors endpoint 4's PublicTransferState terminal states (TA-9.6 enumeration: same bodies) + WrongPassword for the 401.</summary>
public enum UnlockState
{
    /// <summary>The password was correct and an unlock token can be issued.</summary>
    Active,
    /// <summary>The transfer passed its expiry time (410).</summary>
    Expired,
    /// <summary>The transfer reached its maximum number of downloads (409).</summary>
    DownloadLimit,
    /// <summary>No such transfer: unknown or malformed link, or a deleted transfer (404).</summary>
    NotFound,
    /// <summary>The password was wrong or the transfer has none (401).</summary>
    WrongPassword
}
