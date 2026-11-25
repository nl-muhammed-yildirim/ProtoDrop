using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Billing;

/// <summary>Enumerates the stripe webhook outcome values.</summary>
public enum StripeWebhookOutcome
{
    /// <summary>Applied.</summary>
    Applied,
    /// <summary>Duplicate.</summary>
    Duplicate,
    /// <summary>Ignored.</summary>
    Ignored
}
