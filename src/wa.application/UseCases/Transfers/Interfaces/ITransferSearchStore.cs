namespace wa.application.UseCases.Transfers;

/// <summary>US-045-01 (FR-045-1/3/7): owner-scoped transfer search. Owner + not-deleted predicate in the query (search never crosses owners);
/// one row per transfer; order rank DESC, CreatedAtUtc DESC, Id DESC; Take(limit+1); NextCursor = SearchCursorCodec of the last returned row.</summary>
public interface ITransferSearchStore
{
    Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
        Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
        int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct);
}
