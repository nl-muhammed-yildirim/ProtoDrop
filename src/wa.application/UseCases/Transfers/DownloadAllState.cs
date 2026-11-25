namespace wa.application.UseCases.Transfers;

/// <summary>The download-all endpoint's state mapping (TA-4.2#7): terminal states + the auth gate + zip ready/generating.</summary>
public enum DownloadAllState
{
    /// <summary>The zip exists and a download URL was minted.</summary>
    Ok,
    /// <summary>The zip is still being built; the caller should poll again.</summary>
    Generating,
    /// <summary>The transfer has expired (410).</summary>
    Expired,
    /// <summary>The transfer reached its maximum number of downloads (409).</summary>
    DownloadLimit,
    /// <summary>No transfer exists for the link (404).</summary>
    NotFound,
    /// <summary>The transfer is password protected and no valid unlock token was supplied (401).</summary>
    Unauthenticated,
    /// <summary>Blob storage could not be reached, so the zip state is unknown.</summary>
    StorageUnavailable
}
