using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using wa.application.UseCases.Billing;
using wa.infrastructure.Billing;

namespace wa.api.integration;

/// <summary>US-019-01 (AC-019-1 params, AC-019-5): StripeCheckoutGateway over a fake HttpMessageHandler — never real Stripe. No Docker.</summary>
public class StripeCheckoutGatewayTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly StripeCheckoutRequest Request = new(UserId, "price_x", "https://app/ok", "https://app/no");

    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public HttpRequestMessage? Seen { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return Respond(request);
        }
    }

    private static StripeCheckoutGateway Make(FakeHandler handler, string? key = "sk_test_x") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.stripe.test/") }, key, NullLogger<StripeCheckoutGateway>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_PostsExpectedForm_AndParsesSession()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.OK, "{\"id\":\"cs_1\",\"url\":\"https://checkout.stripe.test/c/1\"}") };
        var session = await Make(handler).CreateSubscriptionCheckoutAsync(Request, default);

        Assert.Equal("cs_1", session.SessionId);
        Assert.Equal("https://checkout.stripe.test/c/1", session.Url);
        Assert.Equal(HttpMethod.Post, handler.Seen!.Method);
        Assert.Equal("/v1/checkout/sessions", handler.Seen.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Seen.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_x", handler.Seen.Headers.Authorization.Parameter);

        var form = System.Web.HttpUtility.ParseQueryString(handler.Body!);
        Assert.Equal("subscription", form["mode"]);
        Assert.Equal(UserId.ToString("N"), form["client_reference_id"]);
        Assert.Equal(UserId.ToString("N"), form["subscription_data[metadata][userId]"]);
        Assert.Equal("price_x", form["line_items[0][price]"]);
        Assert.Equal("1", form["line_items[0][quantity]"]);
        Assert.Equal("https://app/ok", form["success_url"]);
        Assert.Equal("https://app/no", form["cancel_url"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    public async Task NonSuccessStatus_BillingUnavailable(HttpStatusCode status)
    {
        var handler = new FakeHandler { Respond = _ => Json(status, "{\"error\":{}}") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreateSubscriptionCheckoutAsync(Request, default));
    }

    [Fact]
    public async Task MissingUrl_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.OK, "{\"id\":\"cs_1\"}") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreateSubscriptionCheckoutAsync(Request, default));
    }

    [Fact]
    public async Task TransportFailure_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => throw new HttpRequestException("boom") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreateSubscriptionCheckoutAsync(Request, default));
    }

    [Fact]
    public async Task Timeout_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => throw new TaskCanceledException("timeout") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreateSubscriptionCheckoutAsync(Request, default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task BlankSecret_BillingUnavailable_NoHttpCall(string? key)
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler, key).CreateSubscriptionCheckoutAsync(Request, default));
        Assert.Null(handler.Seen);
    }

    // ---- US-019-02: portal session ----

    [Fact]
    public async Task Portal_Success_PostsExpectedForm_AndParsesSession()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.OK, "{\"id\":\"bps_1\",\"url\":\"https://billing.stripe.test/p/1\"}") };
        var session = await Make(handler).CreatePortalSessionAsync("cus_1", "https://app/return", default);

        Assert.Equal("bps_1", session.SessionId);
        Assert.Equal("https://billing.stripe.test/p/1", session.Url);
        Assert.Equal(HttpMethod.Post, handler.Seen!.Method);
        Assert.Equal("/v1/billing_portal/sessions", handler.Seen.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Seen.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_x", handler.Seen.Headers.Authorization.Parameter);

        var form = System.Web.HttpUtility.ParseQueryString(handler.Body!);
        Assert.Equal("cus_1", form["customer"]);
        Assert.Equal("https://app/return", form["return_url"]);
    }

    [Fact]
    public async Task Portal_NonSuccessStatus_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.InternalServerError, "{\"error\":{}}") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreatePortalSessionAsync("cus_1", "https://app/return", default));
    }

    [Fact]
    public async Task Portal_TransportFailure_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => throw new HttpRequestException("boom") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreatePortalSessionAsync("cus_1", "https://app/return", default));
    }

    [Fact]
    public async Task Portal_MissingUrl_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.OK, "{\"id\":\"bps_1\"}") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).CreatePortalSessionAsync("cus_1", "https://app/return", default));
    }

    [Fact]
    public async Task Portal_BlankSecret_BillingUnavailable_NoHttpCall()
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler, " ").CreatePortalSessionAsync("cus_1", "https://app/return", default));
        Assert.Null(handler.Seen);
    }

    // ---- US-019-07 (EC-019-4): GET subscription ----

    [Fact]
    public async Task GetSubscription_Success_GetsAndParsesSnapshot()
    {
        var handler = new FakeHandler
        {
            Respond = _ => Json(HttpStatusCode.OK,
                "{\"id\":\"sub_1\",\"customer\":\"cus_1\",\"status\":\"active\",\"metadata\":{\"userId\":\"" + UserId + "\"},"
                + "\"items\":{\"data\":[{\"price\":{\"id\":\"price_pro\"},\"current_period_end\":1900000000}]}}")
        };

        var snap = await Make(handler).GetSubscriptionAsync("sub_1", default);

        Assert.Equal(HttpMethod.Get, handler.Seen!.Method);
        Assert.Equal("/v1/subscriptions/sub_1", handler.Seen.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", handler.Seen.Headers.Authorization!.Scheme);
        Assert.Equal("sk_test_x", handler.Seen.Headers.Authorization.Parameter);
        Assert.NotNull(snap);
        Assert.Equal("active", snap!.Status);
        Assert.Equal("cus_1", snap.CustomerId);
        Assert.Equal("price_pro", snap.PriceId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1900000000).UtcDateTime, snap.CurrentPeriodEndUtc);
        Assert.Equal(UserId, snap.MetadataUserId);
    }

    [Fact]
    public async Task GetSubscription_404_ReturnsNull()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.NotFound, "{\"error\":{}}") };
        Assert.Null(await Make(handler).GetSubscriptionAsync("sub_1", default));
    }

    [Fact]
    public async Task GetSubscription_500_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.InternalServerError, "{\"error\":{}}") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).GetSubscriptionAsync("sub_1", default));
    }

    [Fact]
    public async Task GetSubscription_BlankSecret_BillingUnavailable_NoHttpCall()
    {
        var handler = new FakeHandler();
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler, null).GetSubscriptionAsync("sub_1", default));
        Assert.Null(handler.Seen);
    }

    [Fact]
    public async Task GetSubscription_InvalidJson_BillingUnavailable()
    {
        var handler = new FakeHandler { Respond = _ => Json(HttpStatusCode.OK, "not json") };
        await Assert.ThrowsAsync<BillingUnavailableException>(() => Make(handler).GetSubscriptionAsync("sub_1", default));
    }
}
