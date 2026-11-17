using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-09 (TA-4.2#25 / US-011-04 happy path 5): GET /api/v1/admin/audit-recent — Recent actions list.
// The handler's contract: request the last 50 rows (newest first) via IAuditReadStore — read paths write no audit rows.
public class GetRecentAuditQueryTests
{
    private readonly StubIAuditReadStore _store;
    private readonly GetRecentAuditQueryHandler _handler;

    public GetRecentAuditQueryTests()
    {
        _store = new StubIAuditReadStore();
        _handler = new GetRecentAuditQueryHandler(_store);
    }

    // ---- Exit check: store called with count = 50; result identical to canned newest-first rows ----

    [Fact]
    public async Task ReturnsLast50NewestFirst()
    {
        var newest = new AuditLog { Id = 3, ActorEmail = "a@example.com", Action = "user.force_delete" };
        var middle = new AuditLog { Id = 2, ActorEmail = "b@example.com", Action = "flag.updated" };
        var oldest = new AuditLog { Id = 1, ActorEmail = "c@example.com", Action = "user.plan_changed" };
        _store.Rows = new List<AuditLog> { newest, middle, oldest }; // canned rows in newest-first order

        var result = await _handler.Handle(new GetRecentAuditQuery(), CancellationToken.None);

        // The store was called with count = 50 (US-011-04 happy path 5: last 50).
        Assert.Equal(1, _store.GetCallCount);
        Assert.Equal(50, _store.CapturedCount);

        // Result is identical to the canned rows — newest first, no reordering.
        Assert.Equal(3, result.Count);
        Assert.Same(newest, result[0]);
        Assert.Same(middle, result[1]);
        Assert.Same(oldest, result[2]);
    }

    // ---- Exit check: empty store → empty list, no throw ----

    [Fact]
    public async Task EmptyStore_ReturnsEmptyList()
    {
        _store.Rows = new List<AuditLog>();

        var result = await _handler.Handle(new GetRecentAuditQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    // ---- Stubs (hand-written, no Moq — mirrors SetFlagCommandTests style) ----

    private sealed class StubIAuditReadStore : IAuditReadStore
    {
        public int GetCallCount { get; private set; }
        public int CapturedCount { get; private set; }
        public List<AuditLog> Rows { get; set; } = new();

        public Task<IReadOnlyList<AuditLog>> GetRecentAsync(int count, CancellationToken ct)
        {
            GetCallCount++;
            CapturedCount = count;
            return Task.FromResult<IReadOnlyList<AuditLog>>(Rows);
        }
    }
}
