using MediatR;
using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01 (TA-4.2#18 / FR-019-1): open a Stripe Checkout Session for the session user. Persists nothing.</summary>
public sealed record CreateCheckoutSessionCommand(Guid UserId, string PlanCode, BillingInterval Interval) : IRequest<StripeCheckoutSession>;

/// <summary>US-019-01: wire body bound by Minimal API. The session supplies UserId — not in the body.</summary>
public sealed record CheckoutRequest(string? PlanCode, string? Interval);

/// <summary>US-019-01: house style — private ctor + static factories. NotFound → 404; InvalidPlan / AlreadySubscribed → 400 VALIDATION.</summary>
public sealed class CheckoutException : Exception
{
    public enum FailureCode { NotFound, InvalidPlan, AlreadySubscribed }

    private CheckoutException(FailureCode code, string message) : base(message) => Code = code;

    public FailureCode Code { get; }

    public static CheckoutException ForNotFound() => new(FailureCode.NotFound, "User not found.");

    public static CheckoutException ForInvalidPlan() => new(FailureCode.InvalidPlan, "Choose a paid plan to upgrade to.");

    public static CheckoutException ForAlreadySubscribed() => new(FailureCode.AlreadySubscribed, "Already on a paid plan — use Manage billing.");
}

public sealed class CreateCheckoutSessionHandler : IRequestHandler<CreateCheckoutSessionCommand, StripeCheckoutSession>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IStripeCheckoutGateway _gateway;
    private readonly string _publicUrl;

    public CreateCheckoutSessionHandler(IAppUserStore users, IPlansStore plans, IStripeCheckoutGateway gateway, IConfiguration configuration)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        ArgumentNullException.ThrowIfNull(configuration);
        _publicUrl = (configuration["Wa:Url:Public"] ?? "http://localhost:5173").TrimEnd('/');
    }

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
