using Microsoft.Extensions.Configuration;
using wa.application.UseCases.Billing;
using wa.domain;

namespace wa.application.unit.Billing;

/// <summary>US-019-02 (AC-019-1 portal half, EC no-subscription fallback): GetPortalSessionHandler.</summary>
public class GetPortalSessionHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class Rig
    {
        public StubStore Store { get; } = new();
        public RecordingGateway Gateway { get; } = new();
        public GetPortalSessionHandler Handler { get; }

        public Rig()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Wa:Url:Public"] = "https://app.example.com" })
                .Build();
            Handler = new GetPortalSessionHandler(Store, Gateway, config);
        }
    }

    [Fact]
    public async Task WithCustomer_CallsGatewayWithCustomerAndReturnUrl_ReturnsPortalUrl()
    {
        var rig = new Rig { Store = { CustomerId = "cus_123" } };
        var result = await rig.Handler.Handle(new GetPortalSessionQuery(UserId), default);

        var call = Assert.Single(rig.Gateway.Calls);
        Assert.Equal("cus_123", call.CustomerId);
        Assert.Equal("https://app.example.com/account/profile?portal=return", call.ReturnUrl);
        Assert.Equal("https://billing.stripe.test/p", result.Url);
        Assert.Equal(UserId, rig.Store.LastUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task NoCustomer_UrlNull_GatewayNotCalled(string? customerId)
    {
        var rig = new Rig { Store = { CustomerId = customerId } };
        var result = await rig.Handler.Handle(new GetPortalSessionQuery(UserId), default);

        Assert.Null(result.Url);
        Assert.Empty(rig.Gateway.Calls);
    }

    [Fact]
    public async Task GatewayFailure_PropagatesUnchanged()
    {
        var failure = new BillingUnavailableException("down");
        var rig = new Rig { Store = { CustomerId = "cus_123" }, Gateway = { Failure = failure } };

        var ex = await Assert.ThrowsAsync<BillingUnavailableException>(() => rig.Handler.Handle(new GetPortalSessionQuery(UserId), default));
        Assert.Same(failure, ex);
    }

    private sealed class StubStore : IStripeBillingStore
    {
        public string? CustomerId { get; set; }
        public Guid? LastUserId { get; private set; }

        public Task<string?> FindStripeCustomerIdAsync(Guid userId, CancellationToken ct)
        {
            LastUserId = userId;
            return Task.FromResult(CustomerId);
        }

        public Task<Plan?> FindPlanByStripePriceIdAsync(string priceId, CancellationToken ct) => throw new NotSupportedException();

        public Task<StripeApplyResult> ApplySubscriptionAsync(StripeSubscriptionChange change, Guid? targetPlanId, DateTime nowUtc, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingGateway : IStripeCheckoutGateway
    {
        public List<(string CustomerId, string ReturnUrl)> Calls { get; } = new();
        public Exception? Failure { get; set; }

        public Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct)
        {
            Calls.Add((customerId, returnUrl));
            if (Failure is not null) throw Failure;
            return Task.FromResult(new StripePortalSession("bps_1", "https://billing.stripe.test/p"));
        }

        public Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
