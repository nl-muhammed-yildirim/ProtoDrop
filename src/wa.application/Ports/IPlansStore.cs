using wa.domain;

namespace wa.application.Ports;

/// <summary>Reads seeded plan rows (TA-3.2: dbo.Plan). DB down surfaces as the underlying exception (EC-053-3) — callers map per TA-4.1.3.</summary>
public interface IPlansStore
{
    /// <summary>Returns the seeded row for <paramref name="planCode"/>, or throws <see cref="PlanUnknownException"/> when no such plan exists (AC-053-4).</summary>
    /// <param name="planCode">The plan code.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan.</returns>
    Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken);

    /// <summary>T-008-02: resolve a plan by its Id for LoginResult.PlanCode. Returns the row or null — never throws.</summary>
    /// <param name="planId">Identifier of the plan.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan, or <see langword="null"/> when there is none.</returns>
    Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken);
}
