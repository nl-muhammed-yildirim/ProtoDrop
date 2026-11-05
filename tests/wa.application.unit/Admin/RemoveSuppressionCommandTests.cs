using wa.application.UseCases.Admin;
using wa.domain;

namespace wa.application.unit.Admin;

// T-011-09 (TA-4.2#24 / FR-011-5): DELETE /api/v1/admin/suppressions/{id:guid} — SuperAdmin-only write.
// The handler's contract: pass the command's Id to the store, return the REMOVED row (the endpoint audits
// { email } from it) or null when no row for that Id (endpoint maps to 404 NOT_FOUND).
public class RemoveSuppressionCommandTests
{
    private readonly StubIAdminSuppressionStore _store;
    private readonly RemoveSuppressionCommandHandler _handler;

    public RemoveSuppressionCommandTests()
    {
        _store = new StubIAdminSuppressionStore();
        _handler = new RemoveSuppressionCommandHandler(_store);
    }

    // ---- Exit check: known id → store.RemoveByIdAsync captured with the command's Id; result is the removed row ----

    [Fact]
    public async Task Success_ReturnsRemovedRow()
    {
        var id = Guid.NewGuid();
        const string address = "victim@example.com";
        _store.RemovedRow = new EmailSuppression { Id = id, Address = address };

        var result = await _handler.Handle(new RemoveSuppressionCommand(id), CancellationToken.None);

        // The store was called with the command's Id.
        Assert.Equal(1, _store.RemoveCallCount);
        Assert.Equal(id, _store.CapturedId);

        // Result is the removed row — the endpoint audits { email } from it (FR-011-5).
        Assert.NotNull(result);
        Assert.Equal(address, result!.Address);
    }

    // ---- Exit check: unknown id → store returns null → handler returns null (endpoint maps to 404 NOT_FOUND) ----

    [Fact]
    public async Task UnknownId_ReturnsNull()
    {
        _store.RemovedRow = null; // no row for that Id

        var result = await _handler.Handle(new RemoveSuppressionCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(1, _store.RemoveCallCount); // the store WAS called — the id resolved to no row
    }

    // ---- Stubs (hand-written, no Moq — mirrors SetFlagCommandTests style) ----

    private sealed class StubIAdminSuppressionStore : IAdminSuppressionStore
    {
        public int RemoveCallCount { get; private set; }
        public Guid CapturedId { get; private set; }
        public EmailSuppression? RemovedRow { get; set; }

        public Task<IReadOnlyList<EmailSuppression>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<EmailSuppression>>(new List<EmailSuppression>());

        public Task<EmailSuppression?> RemoveByIdAsync(Guid id, CancellationToken ct)
        {
            RemoveCallCount++;
            CapturedId = id;
            return Task.FromResult(RemovedRow);
        }
    }
}
