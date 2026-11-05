using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-08 (TA-4.2#23 / FR-011-2): GET /api/v1/admin/flags — list all seeded flag rows.
// The handler's contract: return all rows ordered by Key ascending (52 fixed rows, no paging).
public class GetFlagsQueryTests
{
    private readonly StubIFlagsStore _flags;
    private readonly GetFlagsQueryHandler _handler;

    public GetFlagsQueryTests()
    {
        _flags = new StubIFlagsStore();
        _handler = new GetFlagsQueryHandler(_flags);
    }

    // ---- Exit check: GET lists all seeded keys, ordered by Key ascending ----

    [Fact]
    public async Task ReturnsAllRowsOrderedByKey()
    {
        // Stub rows unsorted — the handler must order them.
        _flags.Rows = new List<FeatureFlag>
        {
            new() { Key = "limits.pro.maxFiles", Value = "100" },
            new() { Key = "feature.ads", Value = "true" },
            new() { Key = "limits.free.maxTransferSize", Value = "5368709120" }
        };

        var result = await _handler.Handle(new GetFlagsQuery(), CancellationToken.None);

        // Result contains every key, ordered by Key ascending.
        Assert.Equal(3, result.Count);
        Assert.Equal("feature.ads", result[0].Key);
        Assert.Equal("limits.free.maxTransferSize", result[1].Key);
        Assert.Equal("limits.pro.maxFiles", result[2].Key);
    }

    // ---- Exit check: empty store → empty list, no throw ----

    [Fact]
    public async Task EmptyStore_ReturnsEmptyList()
    {
        _flags.Rows = new List<FeatureFlag>();

        var result = await _handler.Handle(new GetFlagsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    // ---- Stubs (hand-written, no Moq — mirrors SetUserPlanCommandTests style) ----

    private sealed class StubIFlagsStore : IFlagsStore
    {
        public List<FeatureFlag> Rows { get; set; } = new();

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeatureFlag>>(Rows);
    }
}
