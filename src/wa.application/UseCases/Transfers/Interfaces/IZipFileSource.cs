namespace wa.application.UseCases.Transfers;

/// <summary>
/// T-004-01: loads the files for a transfer by its internal Id (not linkId) — used by the f-zip Azure Function.
/// </summary>
public interface IZipFileSource
{
    /// <summary>Return (true, files ordered by SortOrder) or (false, empty) when no row matches.</summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>A tuple holding the found and files values.</returns>
    Task<(bool Found, IReadOnlyList<PublicFileItem> Files)> GetFilesByTransferIdAsync(Guid transferId, CancellationToken ct);
}
