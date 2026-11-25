using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

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
