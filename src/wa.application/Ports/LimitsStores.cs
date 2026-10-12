using wa.domain;

namespace wa.application.Ports;

/// <summary>Typed failure for a plan code with no seeded row — maps to 404 <c>PLAN_UNKNOWN</c> at the API layer (AC-053-4). Never null or an empty record.</summary>
public sealed class PlanUnknownException : Exception
{
    public PlanUnknownException(string planCode)
        : base($"No seeded plan with code '{planCode}'.") => PlanCode = planCode;

    /// <summary>The offending plan code, for logging/telemetry (not a Problem+JSON body field — T-056 owns that shape).</summary>
    public string PlanCode { get; }
}

/// <summary>Reads seeded plan rows (TA-3.2: dbo.Plan). DB down surfaces as the underlying exception (EC-053-3) — callers map per TA-4.1.3.</summary>
public interface IPlansStore
{
    /// <summary>Returns the seeded row for <paramref name="planCode"/>, or throws <see cref="PlanUnknownException"/> when no such plan exists (AC-053-4).</summary>
    Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken);
}

/// <summary>Reads flag rows. Limit resolution consumes the raw rows — key filtering and override layering live in T-053-04.</summary>
public interface IFlagsStore
{
    /// <summary>Returns all seeded FeatureFlag rows (keys + string values, unfiltered).</summary>
    Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken);
}
