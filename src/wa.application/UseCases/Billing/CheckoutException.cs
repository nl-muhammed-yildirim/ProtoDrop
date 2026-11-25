using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01: house style — private ctor + static factories. NotFound → 404; InvalidPlan / AlreadySubscribed → 400 VALIDATION.</summary>
public sealed class CheckoutException : Exception
{
    /// <summary>The reason a checkout could not be started.</summary>
    public enum FailureCode
    {
        /// <summary>The signed-in user no longer exists.</summary>
        NotFound,
        /// <summary>The requested plan is unknown or is the free plan.</summary>
        InvalidPlan,
        /// <summary>The user is already on a paid plan and must use the billing portal instead.</summary>
        AlreadySubscribed
    }

    private CheckoutException(FailureCode code, string message) : base(message) => Code = code;

    /// <summary>Which failure occurred; the endpoint maps it to a problem response.</summary>
    public FailureCode Code { get; }

    /// <summary>Creates the checkout exception for not found.</summary>
    /// <returns>An exception with <see cref="FailureCode.NotFound"/>.</returns>
    public static CheckoutException ForNotFound() => new(FailureCode.NotFound, "User not found.");

    /// <summary>Creates the checkout exception for invalid plan.</summary>
    /// <returns>An exception with <see cref="FailureCode.InvalidPlan"/>.</returns>
    public static CheckoutException ForInvalidPlan() => new(FailureCode.InvalidPlan, "Choose a paid plan to upgrade to.");

    /// <summary>Creates the checkout exception for already subscribed.</summary>
    /// <returns>An exception with <see cref="FailureCode.AlreadySubscribed"/>.</returns>
    public static CheckoutException ForAlreadySubscribed() => new(FailureCode.AlreadySubscribed, "Already on a paid plan — use Manage billing.");
}
