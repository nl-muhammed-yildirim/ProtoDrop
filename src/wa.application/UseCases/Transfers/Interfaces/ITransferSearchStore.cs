namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (FR-045-1/3/7): owner-scoped transfer search. Owner + not-deleted predicate in the query (search never crosses owners);
/// one row per transfer; order rank DESC, CreatedAtUtc DESC, Id DESC; Take(limit+1); NextCursor = SearchCursorCodec of the last returned row.</summary>
public interface ITransferSearchStore
{
    /// <summary>Searches the owner's transfers by file name, recipient address or sender name, ranked by the best match.</summary>
    /// <param name="ownerId">The signed-in user; search never crosses owners.</param>
    /// <param name="term">Normalized search term, matched literally.</param>
    /// <param name="statuses">Status bytes to include; null for all statuses.</param>
    /// <param name="afterRank">Cursor: rank of the last row of the previous page; null for the first page.</param>
    /// <param name="afterUtc">Cursor: CreatedAtUtc of the last row of the previous page.</param>
    /// <param name="afterIdExcluded">Cursor: Id of the last row of the previous page, to break ties.</param>
    /// <param name="limit">Page size.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>One page of rows and the cursor of the next page (null when there is none).</returns>
    Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
        Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
        int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct);
}
