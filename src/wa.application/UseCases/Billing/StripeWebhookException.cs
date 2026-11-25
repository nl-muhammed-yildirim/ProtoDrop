using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>US-019-03: house style — private ctor + static factories. Malformed → 400 VALIDATION; Retryable → 500 so Stripe redelivers (EC-019-1).</summary>
public sealed class StripeWebhookException : Exception
{
    private StripeWebhookException(string message, bool retryable) : base(message) => Retryable = retryable;

    /// <summary>Gets a value indicating whether retryable applies to the stripe webhook exception.</summary>
    public bool Retryable { get; }

    /// <summary>Creates the stripe webhook exception for malformed.</summary>
    /// <returns>The stripe webhook exception.</returns>
    public static StripeWebhookException ForMalformed() =>
        new("Stripe webhook payload is malformed.", retryable: false);

    /// <summary>Message carries Stripe ids only (TA-9.4).</summary>
    /// <param name="message">The message.</param>
    /// <returns>The stripe webhook exception.</returns>
    public static StripeWebhookException ForRetryable(string message) => new(message, retryable: true);
}
