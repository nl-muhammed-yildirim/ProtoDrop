using wa.application.UseCases.Admin;
using wa.domain.Transfers;

namespace wa.application.unit.Admin;

// T-011-06 (TA-4.2#22 / FR-011-2): GetUsersQuery handler — pure composition over IAdminUserStore.
// AC in scope: search gherkin case + cursor paging + q normalization.
// Hand stubs, NO Moq (mirrors SearchTransfersQueryTests style).
public class GetUsersQueryTests
{
    private readonly StubAdminUserStore _store;
    private readonly GetUsersQueryHandler _handler;

    public GetUsersQueryTests()
    {
        _store = new StubAdminUserStore();
        _handler = new GetUsersQueryHandler(_store);
    }

    // ---- Exit check: malformed cursor → CursorFormatException BEFORE the store call (400 VALIDATION before the query runs) ----

    [Fact]
    public async Task MalformedCursor_ThrowsBefore_StoreCall()
    {
        var ex = await Assert.ThrowsAsync<CursorFormatException>(async () =>
            await _handler.Handle(new GetUsersQuery(null, "not-base64", 25), CancellationToken.None));

        Assert.Equal(0, _store.CallCount); // store NOT called — the cursor decode failed first.
    }

    // ---- Exit check: limit passes through to the store (default-25 + reject > 25 boundary lives in the endpoint's param parsing) ----

    [Fact]
    public async Task Limit_PassesThrough_ToStore()
    {
        await _handler.Handle(new GetUsersQuery(null, null, 25), CancellationToken.None);
        Assert.Equal(25, _store.CapturedLimit);

        await _handler.Handle(new GetUsersQuery(null, null, 10), CancellationToken.None);
        Assert.Equal(10, _store.CapturedLimit);
    }

    // ---- Exit check: q trims + lowercases + nulls empty (AppUser.Email is stored lowercased) ----

    [Fact]
    public async Task Q_Trims_Lowercases_AndNullsEmpty()
    {
        // " ADA@X.COM " → captured "ada@x.com".
        await _handler.Handle(new GetUsersQuery(" ADA@X.COM ", null, 25), CancellationToken.None);
        Assert.Equal("ada@x.com", _store.CapturedQ);

        // "" → null.
        await _handler.Handle(new GetUsersQuery("", null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedQ);

        // whitespace-only → null.
        await _handler.Handle(new GetUsersQuery("   ", null, 25), CancellationToken.None);
        Assert.Null(_store.CapturedQ);
    }

    // ---- Exit check: result passes through store rows and nextCursor (empty list = not an error) ----

    [Fact]
    public async Task Result_PassesThrough_StoreRows_AndNextCursor()
    {
        var rows = new List<AdminUserRow>
        {
            new(Guid.NewGuid(), "ada@x.com", "Pro", 4096L, 2, new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc))
        };
        _store.Data = (rows, "next-cursor-token");

        var result = await _handler.Handle(new GetUsersQuery(null, null, 25), CancellationToken.None);

        Assert.Single(result.Items); // canned rows returned verbatim.
        Assert.Equal("ada@x.com", result.Items[0].Email);
        Assert.Equal("Pro", result.Items[0].PlanName);
        Assert.Equal(4096L, result.Items[0].StorageBytesUsed);
        Assert.Equal(2, result.Items[0].ActiveTransferCount);
        Assert.Equal("next-cursor-token", result.NextCursor); // nextCursor returned verbatim.
    }

    [Fact]
    public async Task Result_EmptyItems_NullNextCursor_IsEmptyList_NotError()
    {
        _store.Data = (new List<AdminUserRow>(), null);

        var result = await _handler.Handle(new GetUsersQuery(null, null, 25), CancellationToken.None);

        Assert.Empty(result.Items); // empty list — not an error (no q → full list; no match → empty list).
        Assert.Null(result.NextCursor);
    }

    // ---- Stubs (hand-written, no Moq — mirrors SearchTransfersQueryTests) ----

    private sealed class StubAdminUserStore : IAdminUserStore
    {
        public int CallCount { get; private set; }
        public string? CapturedQ { get; private set; }
        public DateTime? CapturedAfterUtc { get; private set; }
        public Guid? CapturedAfterIdExcluded { get; private set; }
        public int CapturedLimit { get; private set; }
        public (List<AdminUserRow> Items, string? NextCursor) Data { get; set; } = (new List<AdminUserRow>(), null);

        public Task<(List<AdminUserRow> Items, string? NextCursor)> GetUsersAsync(
            string? q,
            DateTime? afterUtc,
            Guid? afterIdExcluded,
            int limit,
            CancellationToken ct)
        {
            CallCount++;
            CapturedQ = q;
            CapturedAfterUtc = afterUtc;
            CapturedAfterIdExcluded = afterIdExcluded;
            CapturedLimit = limit;
            return Task.FromResult(Data);
        }
    }
}
