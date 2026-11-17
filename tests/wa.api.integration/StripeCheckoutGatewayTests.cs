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
}
