using wa.application.UseCases.Admin;
using wa.domain.Transfers;

namespace wa.application.unit.Admin;

// T-011-04 (TA-4.2#21 / FR-011-3): SearchTransfersQuery handler — pure composition over ISearchTransfersStore.
// AC in scope: both search gherkin cases + filter composition + cursor paging.
// Hand stubs, NO Moq (mirrors OverviewSnapshotCacheTests style).
public class SearchTransfersQueryTests
{
    private readonly StubSearchTransfersStore _store;
    private readonly SearchTransfersQueryHandler _handler;

    public SearchTransfersQueryTests()
    {
        _store = new StubSearchTransfersStore();
        _handler = new SearchTransfersQueryHandler(_store);
    }

    // ---- Exit check: malformed cursor → CursorFormatException BEFORE the store call (400 VALIDATION before the query runs) ----

    [Fact]
    public async Task MalformedCursor_ThrowsBefore_StoreCall()
    {
        var ex = await Assert.ThrowsAsync<CursorFormatException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, "not-base64", 25), CancellationToken.None));

        Assert.Equal(0, _store.CallCount); // store NOT called — the cursor decode failed first.
    }

    // ---- Exit check: limit passes through to the store (default-25 + reject > 25 boundary lives in the endpoint's param parsing) ----

    [Fact]
    public async Task Limit_PassesThrough_ToStore()
    {
        await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(25, _store.CapturedLimit);

        await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, null, 10), CancellationToken.None);
        Assert.Equal(10, _store.CapturedLimit);
    }

    // ---- Exit check: status vocabulary resolves byte sets (status filter composes) ----

    [Fact]
    public async Task StatusVocabulary_ResolvesByteSets()
    {
        // absent → null (all).
        await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedStatuses);

        // all → null.
        await _handler.Handle(new SearchTransfersQuery(null, "all", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedStatuses);

        // draft → {0}.
        await _handler.Handle(new SearchTransfersQuery(null, "draft", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(new byte[] { 0 }, _store.CapturedStatuses);

        // active → {1}.
        await _handler.Handle(new SearchTransfersQuery(null, "active", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(new byte[] { 1 }, _store.CapturedStatuses);

        // expired → {2}.
        await _handler.Handle(new SearchTransfersQuery(null, "expired", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(new byte[] { 2 }, _store.CapturedStatuses);

        // download_limit → {3}.
        await _handler.Handle(new SearchTransfersQuery(null, "download_limit", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(new byte[] { 3 }, _store.CapturedStatuses);

        // deleted → {4}.
        await _handler.Handle(new SearchTransfersQuery(null, "deleted", null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal(new byte[] { 4 }, _store.CapturedStatuses);

        // bogus → SearchTransfersValidationException (endpoint maps to 400 VALIDATION).
        await Assert.ThrowsAsync<SearchTransfersValidationException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, "bogus", null, null, null, null, null, 25), CancellationToken.None));
    }

    // ---- Exit check: date range requires both from and to (filter composition) ----

    [Fact]
    public async Task DateRange_RequiresBoth_FromAndTo()
    {
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        // from only → SearchTransfersValidationException.
        await Assert.ThrowsAsync<SearchTransfersValidationException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, null, null, null, from, null, null, 25), CancellationToken.None));

        // to only → SearchTransfersValidationException.
        await Assert.ThrowsAsync<SearchTransfersValidationException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, to, null, 25), CancellationToken.None));

        // both → captured as-is.
        await _handler.Handle(new SearchTransfersQuery(null, null, null, null, from, to, null, 25), CancellationToken.None);
        Assert.Equal(from, _store.CapturedFromUtc);
        Assert.Equal(to, _store.CapturedToUtc);
    }

    // ---- Exit check: size range requires both minBytes and maxBytes (filter composition) ----

    [Fact]
    public async Task SizeRange_RequiresBoth_MinAndMax()
    {
        // minBytes only → SearchTransfersValidationException.
        await Assert.ThrowsAsync<SearchTransfersValidationException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, null, 100L, null, null, null, null, 25), CancellationToken.None));

        // maxBytes only → SearchTransfersValidationException.
        await Assert.ThrowsAsync<SearchTransfersValidationException>(async () =>
            await _handler.Handle(new SearchTransfersQuery(null, null, null, 100L, null, null, null, 25), CancellationToken.None));

        // both → captured as-is.
        await _handler.Handle(new SearchTransfersQuery(null, null, 100L, 200L, null, null, null, 25), CancellationToken.None);
        Assert.Equal(100L, _store.CapturedMinBytes);
        Assert.Equal(200L, _store.CapturedMaxBytes);
    }

    // ---- Exit check: q trims and nulls empty (no q predicate when blank) ----

    [Fact]
    public async Task Q_TrimsAndNullsEmpty()
    {
        // "  ada@x.com  " → captured "ada@x.com".
        await _handler.Handle(new SearchTransfersQuery("  ada@x.com  ", null, null, null, null, null, null, 25), CancellationToken.None);
        Assert.Equal("ada@x.com", _store.CapturedQ);

        // "" → null.
        await _handler.Handle(new SearchTransfersQuery("", null, null, null, null, null, null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedQ);

        // whitespace-only → null.
        await _handler.Handle(new SearchTransfersQuery("   ", null, null, null, null, null, null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedQ);
    }

    // ---- Exit check: result passes through store rows and nextCursor (empty list = not an error) ----

    [Fact]
    public async Task Result_PassesThrough_StoreRows_AndNextCursor()
    {
        var rows = new List<AdminTransferRow>
        {
            new(Guid.NewGuid(), "3f9k2a7x", "ada@x.com", 1, 2, 4096L, new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc), 3)
        };
        _store.Data = (rows, "next-cursor-token");

        var result = await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, null, 25), CancellationToken.None);

        Assert.Single(result.Items); // canned rows returned verbatim.
        Assert.Equal("3f9k2a7x", result.Items[0].LinkId);
        Assert.Equal("ada@x.com", result.Items[0].OwnerEmail);
        Assert.Equal(1, result.Items[0].Status);
        Assert.Equal(4096L, result.Items[0].TotalBytes);
        Assert.Equal("next-cursor-token", result.NextCursor); // nextCursor returned verbatim.
    }

    [Fact]
    public async Task Result_EmptyItems_NullNextCursor_IsEmptyList_NotError()
    {
        _store.Data = (new List<AdminTransferRow>(), null);

        var result = await _handler.Handle(new SearchTransfersQuery(null, null, null, null, null, null, null, 25), CancellationToken.None);

        Assert.Empty(result.Items); // empty list — not an error (US-011-02 Alternative flows).
        Assert.Null(result.NextCursor);
    }

    // ---- Stubs (hand-written, no Moq — mirrors OverviewSnapshotCacheTests) ----

    private sealed class StubSearchTransfersStore : ISearchTransfersStore
    {
        public int CallCount { get; private set; }
        public string? CapturedQ { get; private set; }
        public IReadOnlyCollection<byte>? CapturedStatuses { get; private set; }
        public long? CapturedMinBytes { get; private set; }
        public long? CapturedMaxBytes { get; private set; }
        public DateTime? CapturedFromUtc { get; private set; }
        public DateTime? CapturedToUtc { get; private set; }
        public DateTime? CapturedAfterUtc { get; private set; }
        public Guid? CapturedAfterIdExcluded { get; private set; }
        public int CapturedLimit { get; private set; }
        public (List<AdminTransferRow> Items, string? NextCursor) Data { get; set; } = (new List<AdminTransferRow>(), null);

        public Task<(List<AdminTransferRow> Items, string? NextCursor)> SearchAsync(
            string? q,
            IReadOnlyCollection<byte>? statuses,
            long? minBytes,
            long? maxBytes,
            DateTime? fromUtc,
            DateTime? toUtc,
            DateTime? afterUtc,
            Guid? afterIdExcluded,
            int limit,
            CancellationToken ct)
        {
            CallCount++;
            CapturedQ = q;
            CapturedStatuses = statuses;
            CapturedMinBytes = minBytes;
            CapturedMaxBytes = maxBytes;
            CapturedFromUtc = fromUtc;
            CapturedToUtc = toUtc;
            CapturedAfterUtc = afterUtc;
            CapturedAfterIdExcluded = afterIdExcluded;
            CapturedLimit = limit;
            return Task.FromResult(Data);
        }
    }
}
