namespace wa.domain.Privacy;

/// <summary>Escape hatch for a PII-looking name that holds no personal data (US-048-02); every use is pinned in the convention test so it stays reviewable.</summary>
/// <param name="reason">Why the value is not personal data, e.g. "HMAC of the IP (TA-9.4)".</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NotPiiAttribute(string reason) : Attribute
{
    /// <summary>Why the value is not personal data.</summary>
    public string Reason { get; } = reason;
}
