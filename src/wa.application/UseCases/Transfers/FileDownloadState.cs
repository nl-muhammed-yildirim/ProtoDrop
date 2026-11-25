namespace wa.application.UseCases.Transfers;

/// <summary>The download endpoint's state mapping (TA-4.2#6): terminal states + the auth gate + mint success.</summary>
public enum FileDownloadState
{
    /// <summary>A download URL was minted.</summary>
    Ok,
    /// <summary>The transfer has expired (410).</summary>
    Expired,
    /// <summary>The transfer reached its maximum number of downloads (409).</summary>
    DownloadLimit,
    /// <summary>No transfer or file exists for the link (404).</summary>
    NotFound,
    /// <summary>The transfer is password protected and no valid unlock token was supplied (401).</summary>
    Unauthenticated
}
