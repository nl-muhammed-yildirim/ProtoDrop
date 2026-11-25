namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (FR-045-1/3/7): owner-scoped transfer search. Owner + not-deleted predicate in the query (search never crosses owners);
/// one row per transfer; order rank DESC, CreatedAtUtc DESC, Id DESC; Take(limit+1); NextCursor = SearchCursorCodec of the last returned row.</summary>
public interface ITransferSearchStore
{
    /// <summary>Searches the value tuple of my transfer row items by owner.</summary>
    /// <param name="ownerId">Identifier of the owner.</param>
    /// <param name="term">The term.</param>
    /// <param name="statuses">The statuses.</param>
    /// <param name="afterRank">The after rank.</param>
    /// <param name="afterUtc">The after UTC.</param>
    /// <param name="afterIdExcluded">The after ID excluded.</param>
    /// <param name="limit">The limit.</param>
    /// <param name="ct">Token that cancels the operation.</param>
    /// <returns>A tuple holding the items and next cursor values.</returns>
    Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
        Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
        int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct);
}
