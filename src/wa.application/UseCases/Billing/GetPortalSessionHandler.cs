using MediatR;
using Microsoft.Extensions.Configuration;

namespace wa.application.UseCases.Billing;

/// <summary>Handles <see cref="GetPortalSessionQuery"/>: looks up the user's Stripe customer and asks Stripe for a Billing Portal session URL.</summary>
public sealed class GetPortalSessionHandler : IRequestHandler<GetPortalSessionQuery, PortalSessionResult>
{
    private readonly IStripeBillingStore _store;
    private readonly IStripeCheckoutGateway _gateway;
    private readonly string _publicUrl;

    /// <summary>Initializes a new instance of the <see cref="GetPortalSessionHandler"/> class.</summary>
    /// <param name="store">Store that holds the user's Stripe customer id.</param>
    /// <param name="gateway">Stripe gateway that creates the portal session.</param>
    /// <param name="configuration">Configuration source for the public site URL used as the return link.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public GetPortalSessionHandler(IStripeBillingStore store, IStripeCheckoutGateway gateway, IConfiguration configuration)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(configuration);
        _publicUrl = (configuration["Wa:Url:Public"] ?? "http://localhost:5173").TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<PortalSessionResult> Handle(GetPortalSessionQuery request, CancellationToken ct)
    {
        var customerId = await _store.FindStripeCustomerIdAsync(request.UserId, ct);
        if (string.IsNullOrWhiteSpace(customerId))
            return new PortalSessionResult(null);

        var session = await _gateway.CreatePortalSessionAsync(customerId, $"{_publicUrl}/account/profile?portal=return", ct);
        return new PortalSessionResult(session.Url);
    }
}
