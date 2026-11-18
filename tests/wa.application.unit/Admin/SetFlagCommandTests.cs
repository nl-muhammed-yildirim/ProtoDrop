using wa.application.Ports;
using wa.application.UseCases.Admin;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.unit.Admin;

// T-011-08 / US-044-01 (TA-4.2#23, FR-044-5): PUT /api/v1/admin/flags/{key} — SuperAdmin-only typed flag write.
// Contract: trim → unknown key (FLAG_UNKNOWN) → typed validation (FLAG_INVALID) → upsert via IAdminFlagStore,
// capturing the old value pre-write so the endpoint can audit {old,new} (FR-044-6).
public class SetFlagCommandTests
{
    private const string LimitKey = "limits.free.maxTransferSize";

    private readonly StubIAdminFlagStore _flags;
    private readonly StubIFlagsStore _store;
    private readonly SetFlagCommandHandler _handler;

    public SetFlagCommandTests()
    {
        _flags = new StubIAdminFlagStore();
        _store = new StubIFlagsStore
        {
            Rows =
            {
                new FeatureFlag { Key = LimitKey, Value = "5368709120" },
                new FeatureFlag { Key = "limits.free.maxDownloads", Value = "10" },
                new FeatureFlag { Key = "feature.ads", Value = "false" }
            }
        };
        _handler = new SetFlagCommandHandler(_flags, _store);
    }

    // ---- AC-044-3 (a): unknown key → FLAG_UNKNOWN, store NOT written ----

    [Fact]
    public async Task UnknownKey_Throws_FlagUnknown_UpsertNotCalled()
    {
        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand("limits.free.nonexistent", "123"), CancellationToken.None));

        Assert.Equal(ProblemCodes.FlagUnknown, ex.Code);
        Assert.Equal(0, _flags.UpsertCallCount);
    }

    // ---- Order: unknown is checked before the type, regardless of body ----

    [Fact]
    public async Task UnknownKey_WithBadValue_Throws_FlagUnknown()
    {
        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand("limits.free.nonexistent", "abc"), CancellationToken.None));

        Assert.Equal(ProblemCodes.FlagUnknown, ex.Code);
        Assert.Equal(0, _flags.UpsertCallCount);
    }

    // ---- AC-044-3 (b) / EC-044-1: non-numeric limit → FLAG_INVALID, no write ----

    [Theory]
    [InlineData("abc")]
    [InlineData("\"abc\"")]
    public async Task NonNumericLimit_Throws_FlagInvalid_UpsertNotCalled(string value)
    {
        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand(LimitKey, value), CancellationToken.None));

        Assert.Equal(ProblemCodes.FlagInvalid, ex.Code);
        Assert.Equal(0, _flags.UpsertCallCount);
    }

    // ---- EC-044-5: zero / negative limits rejected ----

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    public async Task NonPositiveLimit_Throws_FlagInvalid(string value)
    {
        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand(LimitKey, value), CancellationToken.None));

        Assert.Equal(ProblemCodes.FlagInvalid, ex.Code);
        Assert.Equal(0, _flags.UpsertCallCount);
    }

    // ---- Happy path: Upsert captured (key, value, now); result carries old + new ----

    [Fact]
    public async Task Success_ReturnsOldAndNew()
    {
        const string oldValue = "5368709120";
        const string newValue = "10737418240";
        _flags.OldRow = new FeatureFlag { Key = LimitKey, Value = oldValue };

        var beforeUtc = DateTime.UtcNow;
        var result = await _handler.Handle(new SetFlagCommand(LimitKey, newValue), CancellationToken.None);
        var afterUtc = DateTime.UtcNow;

        Assert.Equal(1, _flags.UpsertCallCount);
        Assert.Equal(LimitKey, _flags.CapturedKey);
        Assert.Equal(newValue, _flags.CapturedValue);
        Assert.InRange(_flags.CapturedUpdatedAtUtc, beforeUtc, afterUtc);

        Assert.Equal(LimitKey, result.Key);
        Assert.Equal(oldValue, result.OldValue);
        Assert.Equal(newValue, result.NewValue);
    }

    // ---- Value is trimmed before validation and storage ----

    [Fact]
    public async Task Value_IsTrimmedBeforeStore()
    {
        _flags.OldRow = new FeatureFlag { Key = LimitKey, Value = "5368709120" };

        var result = await _handler.Handle(new SetFlagCommand(LimitKey, " 10737418240\n"), CancellationToken.None);

        Assert.Equal("10737418240", _flags.CapturedValue);
        Assert.Equal("10737418240", result.NewValue);
    }

    // ---- Boolean key accepts true/false ----

    [Fact]
    public async Task BooleanKey_AcceptsTrue()
    {
        _flags.OldRow = new FeatureFlag { Key = "feature.ads", Value = "false" };

        var result = await _handler.Handle(new SetFlagCommand("feature.ads", "true"), CancellationToken.None);

        Assert.Equal("true", result.NewValue);
        Assert.Equal("false", result.OldValue);
    }

    // ---- Race: row removed between the existence check and the write → FLAG_UNKNOWN ----

    [Fact]
    public async Task UpsertReturnsNull_Throws_FlagUnknown()
    {
        _flags.OldRow = null;

        var ex = await Assert.ThrowsAsync<SetFlagException>(async () =>
            await _handler.Handle(new SetFlagCommand(LimitKey, "10737418240"), CancellationToken.None));

        Assert.Equal(ProblemCodes.FlagUnknown, ex.Code);
        Assert.Equal(1, _flags.UpsertCallCount);
    }

    // ---- Stubs (hand-written, no Moq) ----

    private sealed class StubIFlagsStore : IFlagsStore
    {
        public List<FeatureFlag> Rows { get; } = new();

        public Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FeatureFlag>>(Rows);
    }

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
