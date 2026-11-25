using wa.domain;

namespace wa.application.Ports;

/// <summary>Reads seeded plan rows (TA-3.2: dbo.Plan). DB down surfaces as the underlying exception (EC-053-3) — callers map per TA-4.1.3.</summary>
public interface IPlansStore
{
    /// <summary>Returns the seeded row for <paramref name="planCode"/>, or throws <see cref="PlanUnknownException"/> when no such plan exists (AC-053-4).</summary>
    /// <param name="planCode">Plan code to load, for example the default plan code.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The seeded plan row.</returns>
    /// <exception cref="PlanUnknownException">Thrown when no plan with that code exists.</exception>
    Task<Plan> GetAsync(string planCode, CancellationToken cancellationToken);

    /// <summary>T-008-02: resolve a plan by its Id for LoginResult.PlanCode. Returns the row or null — never throws.</summary>
    /// <param name="planId">Id of the plan to load.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The plan row, or <see langword="null"/> when no plan has that id.</returns>
    Task<Plan?> GetByIdAsync(Guid planId, CancellationToken cancellationToken);
}
