namespace wa.application.UseCases.Transfers;

/// <summary>The recipient page's state mapping (TA-4.2#4). NotFound covers missing rows + malformed linkIds + Status=4 Deleted — deliberately indistinguishable (FR-003-9).</summary>
public enum PublicTransferState
{
    /// <summary>The transfer can be downloaded; the result carries the full metadata.</summary>
    Active,
    /// <summary>The transfer passed its expiry time (410).</summary>
    Expired,
    /// <summary>The transfer reached its maximum number of downloads (409).</summary>
    DownloadLimit,
    /// <summary>No such transfer: unknown or malformed link, or a deleted transfer (404).</summary>
    NotFound
}
