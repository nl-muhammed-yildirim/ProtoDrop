using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using wa.application.UseCases.Billing;

namespace wa.infrastructure.Billing;

/// <summary>
/// US-019-01 (D-1, TA-7.5 step 1): Stripe Checkout Session creation over plain HttpClient (no Stripe package).
/// Logs only the Stripe status code + request id (TA-9.4: never the key, never an email).
/// </summary>
public sealed class StripeCheckoutGateway : IStripeCheckoutGateway, IStripeSubscriptionSource
{
    private readonly HttpClient _http;
    private readonly string? _secretKey;
    private readonly ILogger<StripeCheckoutGateway> _logger;

    public StripeCheckoutGateway(HttpClient http, string? secretKey, ILogger<StripeCheckoutGateway> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _secretKey = secretKey;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StripeCheckoutSession> CreateSubscriptionCheckoutAsync(StripeCheckoutRequest request, CancellationToken ct)
    {
        var userId = request.UserId.ToString("N");
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["client_reference_id"] = userId,
            ["subscription_data[metadata][userId]"] = userId,
            ["metadata[userId]"] = userId,
            ["line_items[0][price]"] = request.PriceId,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = request.SuccessUrl,
            ["cancel_url"] = request.CancelUrl
        };

        var (id, url) = await PostFormAsync("v1/checkout/sessions", form, "checkout", ct);
        return new StripeCheckoutSession(id, url);
    }

    public async Task<StripePortalSession> CreatePortalSessionAsync(string customerId, string returnUrl, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = returnUrl
        };

        var (id, url) = await PostFormAsync("v1/billing_portal/sessions", form, "portal", ct);
        return new StripePortalSession(id, url);
    }

    /// <summary>US-019-07 (EC-019-4): GET v1/subscriptions/{id}. Null = Stripe 404; any other failure → BillingUnavailableException.</summary>
    public async Task<StripeSubscriptionSnapshot?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"v1/subscriptions/{Uri.EscapeDataString(subscriptionId)}", "subscription", ct);
        if (doc is null)
            return null;

        var snapshot = StripeSubscriptionSnapshot.FromJson(doc.RootElement)
            ?? throw new BillingUnavailableException("Stripe subscription response was missing id/status.");
        return snapshot;
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_secretKey))
            throw new BillingUnavailableException("Stripe secret key is not configured.");

        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);

        try
        {
            using var response = await _http.SendAsync(message, ct);
            var requestId = response.Headers.TryGetValues("Request-Id", out var ids) ? ids.FirstOrDefault() : null;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stripe {Operation} fetch failed: status {StatusCode}, request id {StripeRequestId}",
                    operation, (int)response.StatusCode, requestId);
                throw new BillingUnavailableException($"Stripe returned {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Stripe {Operation} transport failure", operation);
            throw new BillingUnavailableException("Stripe is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Stripe {Operation} timed out", operation);
            throw new BillingUnavailableException("Stripe request timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingUnavailableException("Stripe response was not valid JSON.", ex);
        }
    }

    private async Task<(string Id, string Url)> PostFormAsync(string path, Dictionary<string, string> form, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_secretKey))
            throw new BillingUnavailableException("Stripe secret key is not configured.");

        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);

        try
        {
            using var response = await _http.SendAsync(message, ct);
            var requestId = response.Headers.TryGetValues("Request-Id", out var ids) ? ids.FirstOrDefault() : null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stripe {Operation} session creation failed: status {StatusCode}, request id {StripeRequestId}",
                    operation, (int)response.StatusCode, requestId);
                throw new BillingUnavailableException($"Stripe returned {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            var url = root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String ? urlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("Stripe {Operation} session response missing id/url: request id {StripeRequestId}", operation, requestId);
                throw new BillingUnavailableException("Stripe response did not include a session url.");
            }

            return (id, url);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Stripe {Operation} transport failure", operation);
            throw new BillingUnavailableException("Stripe is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Stripe {Operation} timed out", operation);
            throw new BillingUnavailableException("Stripe request timed out.", ex);
        }
        catch (JsonException ex)
        {
            throw new BillingUnavailableException("Stripe response was not valid JSON.", ex);
        }
    }
}
