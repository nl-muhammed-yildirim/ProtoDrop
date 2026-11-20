using wa.application.Limits;
using wa.application.Ports;
using wa.application.UseCases.Transfers;
using wa.domain;

namespace wa.application.unit.Transfers;

// F-PRF-004 (FR-024-3): adsEnabled = global feature.ads gate AND the sender's CURRENT plan (AC-024-2, AC-024-4, EC-024-6).
public class RecipientAdsTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly StubFlagResolver _flags = new();
    private readonly StubUserPlanStore _users = new();
    private readonly StubFeaturesProvider _features = new();

    private Task<bool> Resolve(Guid? owner) => RecipientAds.ResolveAsync(_flags, _users, _features, owner, CancellationToken.None);

    [Fact]
    public async Task FlagOn_FreeOwner_True()
    {
        _flags.Values[FeatureGates.Ads] = true;
        _users.PlanCode = "free";

        Assert.True(await Resolve(Owner));
    }

    [Fact]
    public async Task FlagOn_ProOwner_False() // AC-024-2
    {
        _flags.Values[FeatureGates.Ads] = true;
        _users.PlanCode = "pro";

        Assert.False(await Resolve(Owner));
    }

    [Fact]
    public async Task FlagOn_BusinessOwner_False() // AC-024-2
    {
        _flags.Values[FeatureGates.Ads] = true;
        _users.PlanCode = "business";

        Assert.False(await Resolve(Owner));
    }

    [Fact]
    public async Task FlagOff_FreeOwner_False_NoPlanLookup() // AC-024-4
    {
        _flags.Values[FeatureGates.Ads] = false;
        _users.PlanCode = "free";

        Assert.False(await Resolve(Owner));
        Assert.Equal(0, _users.Calls);
    }

    [Fact]
    public async Task FlagOff_ProOwner_False() // AC-024-4 x Pro
    {
        _flags.Values[FeatureGates.Ads] = false;
        _users.PlanCode = "pro";

        Assert.False(await Resolve(Owner));
    }

    [Fact]
    public async Task FlagAbsent_FreeOwner_False() // kill-switch fails closed
    {
        _users.PlanCode = "free";

        Assert.False(await Resolve(Owner));
    }

    [Fact]
    public async Task FlagOn_GuestSender_UsesGuestPlan_True()
    {
        _flags.Values[FeatureGates.Ads] = true;

        Assert.True(await Resolve(null));
        Assert.Equal(0, _users.Calls);
        Assert.Equal(ILimitsProvider.GuestPlanCode, _features.LastPlanCode);
    }

    [Fact]
    public async Task FlagOn_OwnerRowMissing_FallsBackToGuestPlan()
    {
        _flags.Values[FeatureGates.Ads] = true;
        _users.PlanCode = null;

        Assert.True(await Resolve(Owner));
        Assert.Equal(ILimitsProvider.GuestPlanCode, _features.LastPlanCode);
    }

    [Fact]
    public async Task SenderDowngrade_NextResolveFollowsCurrentPlan() // EC-024-6
    {
        _flags.Values[FeatureGates.Ads] = true;
        _users.PlanCode = "pro";
        Assert.False(await Resolve(Owner));

        _users.PlanCode = "free";
        Assert.True(await Resolve(Owner));
    }

    private sealed class StubFlagResolver : IFlagResolver
    {
        public Dictionary<string, object> Values { get; } = [];
        public int Calls { get; private set; }

        public Task<T> ResolveFlag<T>(string key, T defaultValue, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Values.TryGetValue(key, out var v) && v is T typed ? typed : defaultValue);
        }
    }

    private sealed class StubUserPlanStore : IUserPlanStore
    {
        public string? PlanCode { get; set; }
        public int Calls { get; private set; }

        public Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken) { Calls++; return Task.FromResult(PlanCode); }
    }

    /// <summary>Test data modelling the spec'd Free row (Ads = true); independent of the seed.</summary>
    private sealed class StubFeaturesProvider : IFeaturesProvider
    {
        public string? LastPlanCode { get; private set; }

        public Task<PlanFeatures> Resolve(string? planCode, CancellationToken cancellationToken = default)
        {
            LastPlanCode = planCode;
            return Task.FromResult(new PlanFeatures { Ads = planCode is "free" });
        }
    }
}
