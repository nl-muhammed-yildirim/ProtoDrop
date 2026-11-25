using MediatR;
using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>Handles <see cref="CreateCheckoutSessionCommand"/>: rejects free or unknown target plans and users who already pay, then asks Stripe for a hosted checkout session and returns its URL.</summary>
public sealed class CreateCheckoutSessionHandler : IRequestHandler<CreateCheckoutSessionCommand, StripeCheckoutSession>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IStripeCheckoutGateway _gateway;
    private readonly string _publicUrl;

    /// <summary>Initializes a new instance of the <see cref="CreateCheckoutSessionHandler"/> class.</summary>
    /// <param name="users">Store used to read the user and the current plan.</param>
    /// <param name="plans">Store used to look up the target plan.</param>
    /// <param name="gateway">Stripe gateway that creates the checkout session.</param>
    /// <param name="configuration">Configuration source for the public site URL used in the return links.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CreateCheckoutSessionHandler(IAppUserStore users, IPlansStore plans, IStripeCheckoutGateway gateway, IConfiguration configuration)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(configuration);
        _publicUrl = (configuration["Wa:Url:Public"] ?? "http://localhost:5173").TrimEnd('/');
    }

    /// <inheritdoc />
    public async Task<StripeCheckoutSession> Handle(CreateCheckoutSessionCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct) ?? throw CheckoutException.ForNotFound();

        Plan target;
        try
        {
            target = await _plans.GetAsync(request.PlanCode, ct);
        }
        catch (PlanUnknownException)
        {
            throw CheckoutException.ForInvalidPlan();
        }
        if (target.Code == Plan.DefaultCode)
            throw CheckoutException.ForInvalidPlan();

        var current = await _plans.GetByIdAsync(user.PlanId, ct);
        if (current is not null && current.Code != Plan.DefaultCode)
            throw CheckoutException.ForAlreadySubscribed();

        var tier = PlanPrice.Read(target).For(request.Interval);
        if (!tier.IsWiredToStripe())
            throw new BillingUnavailableException("Plan price not wired to Stripe.");

        return await _gateway.CreateSubscriptionCheckoutAsync(
            new StripeCheckoutRequest(
                request.UserId,
                tier.StripePriceId!,
                $"{_publicUrl}/account/profile?checkout=success",
                $"{_publicUrl}/account/profile?checkout=cancel"),
            ct);
    }
}
