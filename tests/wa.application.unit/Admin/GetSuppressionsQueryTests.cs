using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-09 (TA-4.2#24 / FR-011-2): GET /api/v1/admin/suppressions — the Suppressions screen list.
// The handler's contract: order rows CreatedAtUtc descending (newest first) — deterministic, unit-testable without EF.
public class GetSuppressionsQueryTests
{
    private readonly StubIAdminSuppressionStore _store;
    private readonly GetSuppressionsQueryHandler _handler;

    public GetSuppressionsQueryTests()
    {
        _store = new StubIAdminSuppressionStore();
        _handler = new GetSuppressionsQueryHandler(_store);
    }

    // ---- Exit check: rows ordered CreatedAtUtc descending (FR-011-2 list: address/sender/date) ----

    [Fact]
    public async Task ReturnsRowsOrderedByCreatedAtDescending()
    {
        // Stub rows out of order — the handler must sort them.
        _store.Rows = new List<EmailSuppression>
        {
            new EmailSuppression { Id = Guid.NewGuid(), Address = "oldest@example.com", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
            new EmailSuppression { Id = Guid.NewGuid(), Address = "newest@example.com", CreatedAtUtc = DateTime.UtcNow },
            new EmailSuppression { Id = Guid.NewGuid(), Address = "middle@example.com", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) },
        };

        var result = await _handler.Handle(new GetSuppressionsQuery(), CancellationToken.None);

        // Newest first — the store's order is NOT preserved.
        Assert.Equal(3, result.Count);
        Assert.Equal("newest@example.com", result[0].Address);
        Assert.Equal("middle@example.com", result[1].Address);
        Assert.Equal("oldest@example.com", result[2].Address);
    }

    // ---- Exit check: empty store → empty list, no throw ----

    [Fact]
    public async Task EmptyStore_ReturnsEmptyList()
    {
        _store.Rows = new List<EmailSuppression>();

        var result = await _handler.Handle(new GetSuppressionsQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    // ---- Stubs (hand-written, no Moq — mirrors SetFlagCommandTests style) ----

    private sealed class StubIAdminSuppressionStore : IAdminSuppressionStore
    {
        public List<EmailSuppression> Rows { get; set; } = new();
        public EmailSuppression? RemovedRow { get; set; }

        public Task<IReadOnlyList<EmailSuppression>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<EmailSuppression>>(Rows);

        public Task<EmailSuppression?> RemoveByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(RemovedRow);
    }
}
