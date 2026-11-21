using wa.application.Limits;
using wa.application.UseCases.Transfers;
using wa.domain;
using wa.domain.Transfers;

namespace wa.application.unit.Transfers;

/// <summary>US-045-01 (FR-045-5, AC-045-3 API half): SearchMyTransfersHandler — gate off/absent → plain list path untouched;
/// on → term rules + search cursor bound to the term. Hand stubs, no Moq (T-008-06 style).</summary>
public class SearchMyTransfersTests
{
    private static readonly Guid OwnerId = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    [Fact]
    public async Task FlagOff_QIgnored_PlainListCalled_SearchNotCalled()
    {
        var store = new StubTransferSearchStore();
        var plain = new StubTransferStore();
        var flags = new StubFlagResolver();   // no Values entry → default false

        var handler = NewHandler(plain, store, flags);
        var result = await handler.Handle(new SearchMyTransfersQuery(OwnerId, "  financial ", null, 25), CancellationToken.None);

        Assert.Equal(1, plain.ListCalls);          // the plain list path ran (q ignored entirely — FR-045-5)
        Assert.Equal(0, store.Calls);              // the search port was never called
    }

    [Fact]
    public async Task FlagOff_TooLongQ_IsIgnored_NoThrow() =>
        await RunFlagOffAsync(q: new string('a', SearchTerm.MaxLength + 1));   // no length check when q is ignored (D5)

    private static async Task RunFlagOffAsync(string? q, StubTransferStore? plain = null)
    {
        var store = new StubTransferSearchStore();
        plain ??= new StubTransferStore();
        var flags = new StubFlagResolver();   // no Values entry → default false (flag absent defaults off — FR-045-5)

        var handler = NewHandler(plain, store, flags);
        await handler.Handle(new SearchMyTransfersQuery(OwnerId, q, null, 25), CancellationToken.None);

        Assert.Equal(1, plain.ListCalls);     // flag absent → off (default false): the plain list path ran.
    }

    [Fact]
    public async Task FlagAbsent_DefaultsOff() => await RunFlagOffAsync(q: "financial");   // no Values entry at all — same as off.

    private static readonly IReadOnlyCollection<byte> Statuses = new byte[] { 1 };

    [Fact]
    public async Task FlagOn_TrimmedTermAndStatusesAndLimitPassedToSearch()
    {
        var store = new StubTransferSearchStore();
        var flags = OnFlags();

        await NewHandler(new StubTransferStore(), store, flags)
            .Handle(new SearchMyTransfersQuery(OwnerId, "  financial ", null, 10, Statuses), CancellationToken.None);

        Assert.Equal("financial", store.Term);          // trimmed (FR-045-1)
        Assert.Same(Statuses, store.Statuses);           // the status byte set is forwarded as-is (AND combination — plan §5)
        Assert.Equal(10, store.Limit);                   // limit passed through untouched
    }

    [Fact]
    public async Task FlagOn_WhitespaceQ_FallsBackToPlainList()
    {
        var plain = new StubTransferStore();
        var store = new StubTransferSearchStore();
        await NewHandler(plain, store, OnFlags()).Handle(new SearchMyTransfersQuery(OwnerId, "   ", null, 25), CancellationToken.None);

        Assert.Equal(1, plain.ListCalls);                // Normalize("   ") → null → the plain list path (D5)
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task FlagOn_101Chars_ThrowsSearchTermTooLong() =>
        await Assert.ThrowsAsync<SearchTermTooLongException>(() => NewHandler(new StubTransferStore(), new StubTransferSearchStore(), OnFlags())
            .Handle(new SearchMyTransfersQuery(OwnerId, new string('a', 101), null, 25), CancellationToken.None));

    [Fact]
    public async Task FlagOn_100Chars_Searches_Boundary()
    {
        var store = new StubTransferSearchStore();
        await NewHandler(new StubTransferStore(), store, OnFlags())
            .Handle(new SearchMyTransfersQuery(OwnerId, new string('a', 100), null, 25), CancellationToken.None);

        Assert.Equal(100, store.Term!.Length);           // exactly MaxLength → searched (EC-045-3 boundary)
    }

    [Fact]
    public async Task FlagOn_SearchCursor_DecodedIntoRankUtcId()
    {
        var utc = new DateTime(2026, 11, 21, 15, 30, 7, DateTimeKind.Utc);
        var id = Guid.NewGuid();
        var cursor = SearchCursorCodec.Encode("financial", 2, utc, id);

        var store = new StubTransferSearchStore();
        await NewHandler(new StubTransferStore(), store, OnFlags())
            .Handle(new SearchMyTransfersQuery(OwnerId, " financial ", cursor, 25), CancellationToken.None);   // term trims to the same one.

        Assert.Equal(2, store.AfterRank);                // rank decoded from the opaque cursor (FR-045-3)
        Assert.Equal(utc, store.AfterUtc);
        Assert.Equal(id, store.AfterIdExcluded);
    }

    [Fact]
    public async Task FlagOn_CursorFromOtherTerm_ThrowsCursorFormat() =>
        await Assert.ThrowsAsync<CursorFormatException>(() => NewHandler(new StubTransferStore(), new StubTransferSearchStore(), OnFlags())
            .Handle(new SearchMyTransfersQuery(OwnerId, "financial", SearchCursorCodec.Encode("other-term", 3, DateTime.UtcNow, Guid.NewGuid()), 25), CancellationToken.None));

    [Fact]
    public async Task FlagOn_PlainCursor_ThrowsCursorFormat() =>
        await Assert.ThrowsAsync<CursorFormatException>(() => NewHandler(new StubTransferStore(), new StubTransferSearchStore(), OnFlags())
            .Handle(new SearchMyTransfersQuery(OwnerId, "financial", CursorCodec.Encode(DateTime.UtcNow, Guid.NewGuid()), 25), CancellationToken.None));

    [Fact]
    public async Task ReadsFlagOncePerRequest()
    {
        var flags = new StubFlagResolver();
        await NewHandler(new StubTransferStore(), new StubTransferSearchStore(), flags)
            .Handle(new SearchMyTransfersQuery(OwnerId, "financial", null, 25), CancellationToken.None);

        Assert.Equal(1, flags.Calls);                     // the flag is read once per request (D5).
    }

    private static StubFlagResolver OnFlags() => new() { Values = { [FeatureGates.Search] = true } };

    private static SearchMyTransfersHandler NewHandler(ITransferStore plain, ITransferSearchStore store, IFlagResolver flags) =>
        new(plain, store, flags);

    // ---- Stubs (T-008-06 style — hand-written, no Moq; copied from ListMyTransfersTests with a call counter added) ----
    private sealed class StubTransferStore : ITransferStore
    {
        public int ListCalls { get; private set; }

        // ITransferStore members the handler doesn't need.
        public Task<IdempotencyKey?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) => Task.FromResult<IdempotencyKey?>(null);
        public Task<bool> TransferExistsByLinkIdAsync(string linkId, CancellationToken ct) => Task.FromResult(false);
        public Task SaveFinalizeAsync(Transfer transfer, IReadOnlyList<BlobRef> blobRefs, IReadOnlyList<FileItem> fileItems, IdempotencyKey idempotencyKey, CancellationToken ct) => Task.CompletedTask;
        public Task<long> GetActiveStorageBytesAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0L);
        public Task<int> GetActiveTransferCountAsync(Guid ownerId, CancellationToken ct) => Task.FromResult(0);

        /// <summary>The plain list path — the handler under test only needs this member to be callable; rows are not asserted.</summary>
        public Task<(List<MyTransferRow> Items, string? NextCursor)> ListByOwnerAsync(Guid ownerId, DateTime? afterUtc, Guid? afterIdExcluded, int limit, IReadOnlyCollection<byte>? statuses, CancellationToken ct)
        {
            ListCalls++;
            var empty = (Items: new List<MyTransferRow>(), NextCursor: (string?)null);   // explicit null type — tuple inference.
            return Task.FromResult(empty);
        }

        public Task<GetMyTransferResult?> GetByOwnerAsync(Guid ownerId, Guid id, CancellationToken ct) =>
            Task.FromResult<GetMyTransferResult?>(null);

        public Task<MarkDeleteResult?> MarkDeletedAsync(Guid id, Guid ownerId, DateTime deletedAtUtc, CancellationToken ct) =>
            Task.FromResult<MarkDeleteResult?>(null);   // signature-only — not exercised here.
    }

    private sealed class StubTransferSearchStore : ITransferSearchStore
    {
        public int Calls { get; private set; }
        public Guid? OwnerId { get; private set; }
        public string? Term { get; private set; }
        public IReadOnlyCollection<byte>? Statuses { get; private set; }
        public int? AfterRank { get; private set; }
        public DateTime? AfterUtc { get; private set; }
        public Guid? AfterIdExcluded { get; private set; }
        public int Limit { get; private set; }

        /// <summary>Settable result the handler returns verbatim.</summary>
        public (List<MyTransferRow> Items, string? NextCursor) Data = default;

        public Task<(List<MyTransferRow> Items, string? NextCursor)> SearchByOwnerAsync(
            Guid ownerId, string term, IReadOnlyCollection<byte>? statuses,
            int? afterRank, DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
        {
            Calls++;
            OwnerId = ownerId; Term = term; Statuses = statuses; AfterRank = afterRank;
            AfterUtc = afterUtc; AfterIdExcluded = afterIdExcluded; Limit = limit;
            return Task.FromResult(Data);
        }
    }

    private sealed class StubFlagResolver : IFlagResolver   // copied from GetPublicTransferHandlerTests (L652-662).
    {
        public Dictionary<string, object> Values { get; set; } = new();
        public int Calls { get; private set; }

        public Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Values.TryGetValue(key, out var v) && v is T typed ? typed : defaultValue);
        }
    }
}
