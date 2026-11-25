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

    /// <summary>The signed-in user no longer exists; the endpoint maps this to 404 NOT_FOUND.</summary>
    /// <returns>An exception with <see cref="FailureCode.NotFound"/>.</returns>
    public static CheckoutException ForNotFound() => new(FailureCode.NotFound, "User not found.");

    /// <summary>The requested plan is unknown or free; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception with <see cref="FailureCode.InvalidPlan"/>.</returns>
    public static CheckoutException ForInvalidPlan() => new(FailureCode.InvalidPlan, "Choose a paid plan to upgrade to.");

    /// <summary>The user already pays for a plan and should use the billing portal; the endpoint maps this to 400 VALIDATION.</summary>
    /// <returns>An exception with <see cref="FailureCode.AlreadySubscribed"/>.</returns>
    public static CheckoutException ForAlreadySubscribed() => new(FailureCode.AlreadySubscribed, "Already on a paid plan — use Manage billing.");
}
