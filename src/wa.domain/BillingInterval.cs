using System.Text.Json.Serialization;

namespace wa.domain;

/// <summary>The user's billing-interval toggle (AC-018-4).</summary>
public enum BillingInterval
{
    Monthly,
    Annual
}
