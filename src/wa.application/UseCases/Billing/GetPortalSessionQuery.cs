using MediatR;
using Microsoft.Extensions.Configuration;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-02 (TA-4.2#18): open a Stripe Billing Portal session for the session user's customer. Persists nothing.</summary>
public sealed record GetPortalSessionQuery(Guid UserId) : IRequest<PortalSessionResult>;

/// <summary>Url null means no Stripe customer on file — the UI falls back to Upgrade.</summary>
public sealed record PortalSessionResult(string? Url);

public sealed class GetPortalSessionHandler : IRequestHandler<GetPortalSessionQuery, PortalSessionResult>
{
    private readonly IStripeBillingStore _store;
    private readonly IStripeCheckoutGateway _gateway;
    private readonly string _publicUrl;

    public GetPortalSessionHandler(IStripeBillingStore store, IStripeCheckoutGateway gateway, IConfiguration configuration)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(configuration);
        _publicUrl = (configuration["Wa:Url:Public"] ?? "http://localhost:5173").TrimEnd('/');
    }

    public async Task<PortalSessionResult> Handle(GetPortalSessionQuery request, CancellationToken ct)
    {
        var customerId = await _store.FindStripeCustomerIdAsync(request.UserId, ct);
        if (string.IsNullOrWhiteSpace(customerId))
            return new PortalSessionResult(null);

        var session = await _gateway.CreatePortalSessionAsync(customerId, $"{_publicUrl}/account/profile?portal=return", ct);
        return new PortalSessionResult(session.Url);
    }
}
