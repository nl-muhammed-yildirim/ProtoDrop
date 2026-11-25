using Microsoft.Extensions.Configuration;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-01: house style — private ctor + static factories. NotFound → 404; InvalidPlan / AlreadySubscribed → 400 VALIDATION.</summary>
public sealed class CheckoutException : Exception
{
    /// <summary>Enumerates the failure code values.</summary>
    public enum FailureCode
    {
        /// <summary>Not found.</summary>
        NotFound,
        /// <summary>Invalid plan.</summary>
        InvalidPlan,
        /// <summary>Already subscribed.</summary>
        AlreadySubscribed
    }

    private CheckoutException(FailureCode code, string message) : base(message) => Code = code;

    /// <summary>Gets the code of the checkout exception.</summary>
    public FailureCode Code { get; }

    /// <summary>Creates the checkout exception for not found.</summary>
    /// <returns>The checkout exception.</returns>
    public static CheckoutException ForNotFound() => new(FailureCode.NotFound, "User not found.");

    /// <summary>Creates the checkout exception for invalid plan.</summary>
    /// <returns>The checkout exception.</returns>
    public static CheckoutException ForInvalidPlan() => new(FailureCode.InvalidPlan, "Choose a paid plan to upgrade to.");

    /// <summary>Creates the checkout exception for already subscribed.</summary>
    /// <returns>The checkout exception.</returns>
    public static CheckoutException ForAlreadySubscribed() => new(FailureCode.AlreadySubscribed, "Already on a paid plan — use Manage billing.");
}
