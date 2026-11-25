using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.Limits;
using wa.application.unit;
using wa.application.UseCases.EmailSettings;

namespace wa.application.unit.EmailSettings;

/// <summary>F-XCT-003 (AC-046-2, EC-046-1/4, FR-046-5): OwnerNotificationGate — guest / flag-off / column states.</summary>
public class OwnerNotificationGateTests
{
    private static readonly CancellationToken Ct = default;

    [Fact]
    public async Task Guest_NullOwner_MaySend_NoStoreRead() // FR-046-5: no setting for guests
    {
        var store = new StubEmailNotificationsStore();
        var gate = Build(store, flagOn: true);

        Assert.True(await gate.MaySendAsync(null, Ct));
        Assert.Equal(0, store.Reads); // never touches the column
    }

    [Fact]
    public async Task FlagOff_StoredFalse_MaySend_NoStoreRead() // FR-046-6 / EC-046-4: flag off = column inert
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();
        store.Stored[owner] = false;

        Assert.True(await Build(store, flagOn: false).MaySendAsync(owner, Ct));
        Assert.Equal(0, store.Reads); // no read while the gate is off
    }

    [Fact]
    public async Task FlagKeyAbsent_StoredFalse_MaySend_DefaultWhenAbsent() // EC-046-4 via FeatureGates.DefaultWhenAbsent = false
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();
        store.Stored[owner] = false;

        Assert.True(await Build(store, flagOn: null).MaySendAsync(owner, Ct)); // key absent → default (false)
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task FlagOn_StoredNull_MaySend() // FR-046-1: never touched = on
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();

        Assert.True(await Build(store, flagOn: true).MaySendAsync(owner, Ct));
    }

    [Fact]
    public async Task FlagOn_StoredTrue_MaySend() // AC-046-1 scenario 2: turned back on → sent
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();
        store.Stored[owner] = true;

        Assert.True(await Build(store, flagOn: true).MaySendAsync(owner, Ct));
    }

    [Fact]
    public async Task FlagOn_StoredFalse_Suppressed_LogsInformationOnce() // AC-046-2 / TA-9.4: id only, never an email
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();
        store.Stored[owner] = false;
        var log = new CapturingLogger<OwnerNotificationGate>();

        Assert.False(await Build(store, flagOn: true, log).MaySendAsync(owner, Ct));
        Assert.Single(log.Entries);
        var entry = log.Entries[0];
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("suppressedReason=email_setting", entry.Message);
        Assert.Contains(owner.ToString(), entry.Message); // the owner id (a Guid) is logged…
        Assert.DoesNotContain("@", entry.Message); // …and no email address ever appears
    }

    [Fact]
    public async Task ReadsSettingOnEveryCall() // EC-046-1: send-time read, one-message window — no cache
    {
        var owner = Guid.NewGuid();
        var store = new StubEmailNotificationsStore();
        store.Stored[owner] = false;
        var gate = Build(store, flagOn: true);

        Assert.False(await gate.MaySendAsync(owner, Ct));
        store.Stored[owner] = true; // flipped between sends (PATCH /auth/me)
        Assert.True(await gate.MaySendAsync(owner, Ct));
        Assert.Equal(2, store.Reads);
    }

    private static OwnerNotificationGate Build(StubEmailNotificationsStore store, bool? flagOn, ILogger<OwnerNotificationGate>? log = null)
    {
        var flags = new StubFlagResolver();
        if (flagOn is not null)
            flags.Values[FeatureGates.EmailSettings] = flagOn.Value; // key absent → DefaultWhenAbsent (false)
        return new OwnerNotificationGate(store, flags, log ?? NullLogger<OwnerNotificationGate>.Instance);
    }

    /// <summary>Copy of the GetPublicTransferHandlerTests stub — a missing key yields defaultValue (DefaultWhenAbsent).</summary>
    private sealed class StubFlagResolver : IFlagResolver
    {
        public Dictionary<string, object> Values { get; } = [];
        public int Calls { get; private set; }

        public Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Values.TryGetValue(key, out var v) && v is T typed ? typed : defaultValue);
        }
    }
}
