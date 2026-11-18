using wa.application.UseCases.Admin;
using wa.domain.Transfers;

namespace wa.application.unit.Admin;

// US-047-03 (FR-047-6): GetErasuresQuery handler — masking, sweep status, cursor. Hand stubs, no Moq.
public class GetErasuresQueryTests
{
    private static readonly DateTime T = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private sealed class StubStore : IErasureReportStore
    {
        public List<ErasureReportRow> Rows { get; } = [];
        public string? NextCursor { get; set; }
        public int Calls { get; private set; }
        public DateTime? AfterUtc { get; private set; }
        public Guid? AfterId { get; private set; }
        public int Limit { get; private set; }

        public Task<(IReadOnlyList<ErasureReportRow> Items, string? NextCursor)> GetPageAsync(
            DateTime? afterUtc, Guid? afterIdExcluded, int limit, CancellationToken ct)
        {
            Calls++;
            AfterUtc = afterUtc;
            AfterId = afterIdExcluded;
            Limit = limit;
            return Task.FromResult(((IReadOnlyList<ErasureReportRow>)Rows, NextCursor));
        }
    }

    private static (StubStore Store, GetErasuresQueryHandler Handler) Make()
    {
        var store = new StubStore();
        return (store, new GetErasuresQueryHandler(store));
    }

    [Fact]
    public async Task RevealFalse_MasksEveryEmail()
    {
        var (store, h) = Make();
        store.Rows.Add(new(Guid.NewGuid(), "dana@x.com", T, 1, null));
        store.Rows.Add(new(Guid.NewGuid(), "bob@y.org", T, 0, null));

        var r = await h.Handle(new GetErasuresQuery(null, 25, false), CancellationToken.None);

        Assert.Equal(["d****@x.com", "b****@y.org"], r.Items.Select(i => i.Email));
    }

    [Fact]
    public async Task RevealTrue_ReturnsFullEmail()
    {
        var (store, h) = Make();
        store.Rows.Add(new(Guid.NewGuid(), "dana@x.com", T, 1, null));

        var r = await h.Handle(new GetErasuresQuery(null, 25, true), CancellationToken.None);

        Assert.Equal("dana@x.com", Assert.Single(r.Items).Email);
    }

    [Fact]
    public async Task NullSweptAt_IsPending_SetSweptAt_IsSwept_WithTime()
    {
        var (store, h) = Make();
        var swept = T.AddHours(30);
        store.Rows.Add(new(Guid.NewGuid(), "a@x.com", T, 0, null));
        store.Rows.Add(new(Guid.NewGuid(), "b@x.com", T, 0, swept));

        var r = await h.Handle(new GetErasuresQuery(null, 25, true), CancellationToken.None);

        Assert.Equal(ErasureReportPolicy.StatusPending, r.Items[0].SweepStatus);
        Assert.Null(r.Items[0].SweptAtUtc);
        Assert.Equal(ErasureReportPolicy.StatusSwept, r.Items[1].SweepStatus);
        Assert.Equal(swept, r.Items[1].SweptAtUtc);
    }

    [Fact]
    public async Task ZeroReHomed_PassesThroughAsZero()
    {
        var (store, h) = Make();
        store.Rows.Add(new(Guid.NewGuid(), "a@x.com", T, 0, null));

        var r = await h.Handle(new GetErasuresQuery(null, 25, false), CancellationToken.None);

        Assert.Equal(0, Assert.Single(r.Items).ReHomedTransferCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullEmail_StaysNull_InBothModes(bool reveal)
    {
        var (store, h) = Make();
        store.Rows.Add(new(Guid.NewGuid(), null, T, 2, null));

        var r = await h.Handle(new GetErasuresQuery(null, 25, reveal), CancellationToken.None);

        Assert.Null(Assert.Single(r.Items).Email);
    }

    [Fact]
    public async Task Cursor_DecodedAndForwarded()
    {
        var (store, h) = Make();
        var id = Guid.NewGuid();
        var cursor = CursorCodec.Encode(T, id);

        await h.Handle(new GetErasuresQuery(cursor, 10, false), CancellationToken.None);

        Assert.Equal(T, store.AfterUtc);
        Assert.Equal(id, store.AfterId);
        Assert.Equal(10, store.Limit);
    }

    [Fact]
    public async Task NoCursor_ForwardsNulls_AndLimit()
    {
        var (store, h) = Make();

        await h.Handle(new GetErasuresQuery(null, ErasureReportPolicy.PageSize, false), CancellationToken.None);

        Assert.Null(store.AfterUtc);
        Assert.Null(store.AfterId);
        Assert.Equal(ErasureReportPolicy.PageSize, store.Limit);
    }

    [Fact]
    public async Task MalformedCursor_ThrowsCursorFormatException()
    {
        var (store, h) = Make();

        await Assert.ThrowsAsync<CursorFormatException>(() =>
            h.Handle(new GetErasuresQuery("not-base64", 25, false), CancellationToken.None));

        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task NextCursor_PassedThrough()
    {
        var (store, h) = Make();
        store.NextCursor = "abc";

        var r = await h.Handle(new GetErasuresQuery(null, 25, false), CancellationToken.None);

        Assert.Equal("abc", r.NextCursor);
    }
}
