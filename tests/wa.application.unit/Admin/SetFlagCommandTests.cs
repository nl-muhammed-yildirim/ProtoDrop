using wa.application.UseCases.Admin;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.unit.Admin;

// T-011-08 (TA-4.2#23 / FR-011-2): PUT /api/v1/admin/flags/{key} — SuperAdmin-only flag value write.
// The handler's contract: validate JSON FIRST (no DB call on bad JSON), then upsert via IAdminFlagStore,
// capturing the old value pre-write so the endpoint can audit {old,new} (FR-011-5).
public class SetFlagCommandTests
{
    private readonly StubIAdminFlagStore _flags;
    private readonly SetFlagCommandHandler _handler;

    public SetFlagCommandTests()
    {
        _flags = new StubIAdminFlagStore();
        _handler = new SetFlagCommandHandler(_flags);
    }

    // ---- Exit check: bad JSON → ForInvalidJson (Code VALIDATION), store NOT called (EC-011-4) ----

    [Fact]
    public async Task BadJson_Throws_ForInvalidJson_StoreNotCalled()
    {
        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand("limits.free.maxTransferSize", "{not json"), CancellationToken.None));

        // EC-011-4: old value stays live — the store was NOT called (validation happens before any DB call).
        Assert.Equal(ProblemCodes.Validation, ex.Code);
        Assert.Equal("Invalid JSON", ex.Message);
        Assert.Equal(0, _flags.UpsertCallCount);
    }

    // ---- Exit check: unknown key → ForNotFound (Code NOT_FOUND) ----

    [Fact]
    public async Task UnknownKey_Throws_ForNotFound()
    {
        _flags.OldRow = null; // no row for that key

        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand("no.such.key", "123"), CancellationToken.None));

        Assert.Equal(ProblemCodes.NotFound, ex.Code);
        Assert.Equal(1, _flags.UpsertCallCount); // the store WAS called — the key resolved to no row
    }

    // ---- Exit check: happy path — UpsertAsync captured with (key, newValue, nowUtc); result carries old + new ----

    [Fact]
    public async Task Success_ReturnsOldAndNew()
    {
        const string oldValue = "5368709120";
        const string newValue = "10737418240";
        _flags.OldRow = new FeatureFlag { Key = "limits.free.maxTransferSize", Value = oldValue };

        var beforeUtc = DateTime.UtcNow;
        var result = await _handler.Handle(new SetFlagCommand("limits.free.maxTransferSize", newValue), CancellationToken.None);
        var afterUtc = DateTime.UtcNow;

        // The store was called with (key, newValue, nowUtc).
        Assert.Equal(1, _flags.UpsertCallCount);
        Assert.Equal("limits.free.maxTransferSize", _flags.CapturedKey);
        Assert.Equal(newValue, _flags.CapturedValue);
        Assert.InRange(_flags.CapturedUpdatedAtUtc, beforeUtc, afterUtc);

        // Result carries old + new values (the endpoint audits {old,new} from this).
        Assert.Equal("limits.free.maxTransferSize", result.Key);
        Assert.Equal(oldValue, result.OldValue);
        Assert.Equal(newValue, result.NewValue);
    }

    // ---- Exit check: old value is read from the pre-update row (audit {old,new} depends on it; EC-011-1) ----

    [Fact]
    public async Task Success_ReadsOldBeforeWrite()
    {
        const string oldValue = "5368709120";
        const string newValue = "10737418240";
        _flags.OldRow = new FeatureFlag { Key = "limits.free.maxTransferSize", Value = oldValue };

        var result = await _handler.Handle(new SetFlagCommand("limits.free.maxTransferSize", newValue), CancellationToken.None);

        // OldValue is the pre-update value — NOT the incoming value.
        Assert.Equal(oldValue, result.OldValue);
        Assert.NotEqual(result.NewValue, result.OldValue); // old and new are distinct
    }

    // ---- Stubs (hand-written, no Moq — mirrors SetUserPlanCommandTests style) ----

    private sealed class StubIAdminFlagStore : IAdminFlagStore
    {
        public int UpsertCallCount { get; private set; }
        public string CapturedKey { get; private set; } = null!;
        public string CapturedValue { get; private set; } = null!;
        public DateTime CapturedUpdatedAtUtc { get; private set; }
        public FeatureFlag? OldRow { get; set; }

        public Task<FeatureFlag?> UpsertAsync(string key, string value, DateTime updatedAtUtc, CancellationToken ct)
        {
            UpsertCallCount++;
            CapturedKey = key;
            CapturedValue = value;
            CapturedUpdatedAtUtc = updatedAtUtc;
            return Task.FromResult(OldRow);
        }
    }
}
