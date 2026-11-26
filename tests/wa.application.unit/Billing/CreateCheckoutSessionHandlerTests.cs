using System.Text.Json;
using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-01 (FR-019-1, AC-019-1 handler half, AC-019-5): CreateCheckoutSessionHandler.</summary>
public class CreateCheckoutSessionHandlerTests
{
    private const string MonthlyPrice = "price_pro_monthly";
    private const string AnnualPrice = "price_pro_annual";
    private static readonly Guid FreeId = Guid.NewGuid();
    private static readonly Guid ProId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class Rig
    {
        public StubUsers Users { get; } = new();
        public StubPlans Plans { get; } = new();
        public RecordingGateway Gateway { get; } = new();
        public CreateCheckoutSessionHandler Handler { get; }

        public Rig(string? monthly = MonthlyPrice, string? annual = AnnualPrice)
        {
            Plans.Add(new Plan { Id = FreeId, Code = Plan.DefaultCode, PriceJson = Price(null, null) });
            Plans.Add(new Plan { Id = ProId, Code = "pro", PriceJson = Price(monthly, annual) });
            Users.User = new AppUser { Id = UserId, Email = "a@x.com", DisplayName = "A", PlanId = FreeId, Theme = "system" };
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Url:Public"] = "https://app.example.com" })
                .Build();
            Handler = new CreateCheckoutSessionHandler(Users, Plans, Gateway, config);
        }
    }

    private static string Price(string? monthly, string? annual) => JsonSerializer.Serialize(new PlanPrice
    {
        Currency = "usd",
        Monthly = new PlanPriceTier { Amount = 900, StripePriceId = monthly },
        Annual = new PlanPriceTier { Amount = 9000, StripePriceId = annual }
    });

    [Fact]
    public async Task Monthly_SendsMonthlyPriceAndUrlsUnderPublicUrl()
    {
        var rig = new Rig();
        var session = await rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Monthly), default);

        var call = Assert.Single(rig.Gateway.Calls);
        Assert.Equal(UserId, call.UserId);
        Assert.Equal(MonthlyPrice, call.PriceId);
        Assert.Equal("https://app.example.com/account/profile?checkout=success", call.SuccessUrl);
        Assert.Equal("https://app.example.com/account/profile?checkout=cancel", call.CancelUrl);
        Assert.Equal("https://checkout.stripe.test/s", session.Url);
    }

    [Fact]
    public async Task Annual_SendsAnnualPrice()
    {
        var rig = new Rig();
        await rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Annual), default);
        Assert.Equal(AnnualPrice, Assert.Single(rig.Gateway.Calls).PriceId);
    }

    [Fact]
    public async Task AlreadyOnPaidPlan_Refused_GatewayNotCalled()
    {
        var rig = new Rig();
        rig.Users.User!.PlanId = ProId;
        var ex = await Assert.ThrowsAsync<CheckoutException>(() =>
            rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Monthly), default));
        Assert.Equal(CheckoutException.FailureCode.AlreadySubscribed, ex.Code);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("nope")]
    public async Task FreeOrUnknownPlan_InvalidPlan_GatewayNotCalled(string planCode)
    {
        var rig = new Rig();
        var ex = await Assert.ThrowsAsync<CheckoutException>(() =>
            rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, planCode, BillingInterval.Monthly), default));
        Assert.Equal(CheckoutException.FailureCode.InvalidPlan, ex.Code);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task UnknownUser_NotFound()
    {
        var rig = new Rig();
        rig.Users.User = null;
        var ex = await Assert.ThrowsAsync<CheckoutException>(() =>
            rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Monthly), default));
        Assert.Equal(CheckoutException.FailureCode.NotFound, ex.Code);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task UnwiredTier_BillingUnavailable_GatewayNotCalled()
    {
        var rig = new Rig(annual: null);
        await Assert.ThrowsAsync<BillingUnavailableException>(() =>
            rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Annual), default));
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task GatewayOutage_PropagatesUnchanged()
    {
        var rig = new Rig();
        var outage = new BillingUnavailableException("down");
        rig.Gateway.Failure = outage;
        var ex = await Assert.ThrowsAsync<BillingUnavailableException>(() =>
            rig.Handler.Handle(new CreateCheckoutSessionCommand(UserId, "pro", BillingInterval.Monthly), default));
        Assert.Same(outage, ex);
    }

    private sealed class StubUsers : IAppUserStore
    {
        public AppUser? User { get; set; }
        public Task<AppUser?> GetByIdAsync(Guid userId, CancellationToken ct) => Task.FromResult(User);
        public Task<AppUser?> FindByEmailAsync(string lowercasedEmail, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateAsync(AppUser user, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid userId, string? displayName, string? theme, CancellationToken ct) => throw new NotSupportedException();
        public Task ConfirmEmailAndClearPasswordAsync(Guid userId, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> SoftDeleteWithReHomeAsync(Guid userId, DateTime deletedAtUtc, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubPlans : IPlansStore
    {
        private readonly List<Plan> _plans = new();
        public void Add(Plan plan) => _plans.Add(plan);

        public Task<Plan> GetAsync(string planCode, CancellationToken ct) =>
            Task.FromResult(_plans.FirstOrDefault(p => p.Code == planCode) ?? throw new PlanUnknownException(planCode));

        public Task<Plan?> GetByIdAsync(Guid planId, CancellationToken ct) =>
            Task.FromResult(_plans.FirstOrDefault(p => p.Id == planId));
    }

    private sealed class RecordingGateway : IStripeCheckoutGateway
    {
        public List<StripeCheckoutRequest> Calls { get; } = new();
        public Exception? Failure { get; set; }

        public Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct)
        {
            Calls.Add(request);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new StripeCheckoutSession("cs_test_1", "https://checkout.stripe.test/s"));
        }

        public Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
