namespace wa.application.Ports;

/// <summary>Typed failure for a plan code with no seeded row — surfaces as 500 INTERNAL until the EC-018-3 Problem code is decided (AC-053-4). Never null or an empty record.</summary>
public sealed class PlanUnknownException : Exception
{
    public PlanUnknownException(string planCode)
        : base($"No seeded plan with code '{planCode}'.") => PlanCode = planCode;

    /// <summary>The offending plan code, for logging/telemetry (not a Problem+JSON body field — T-056 owns that shape).</summary>
    public string PlanCode { get; }
}
