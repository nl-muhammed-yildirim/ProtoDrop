namespace wa.application.Ports;

/// <summary>US-020-01 (FR-020-1): resolves a user's plan code for PlanContext. Includes soft-deleted users (EC-020-3: last-known plan); null = no user row.</summary>
public interface IUserPlanStore
{
    /// <summary>Returns the code of the plan the user row points at (soft-deleted users included), or null when no such user row exists.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The resulting text.</returns>
    Task<string?> GetPlanCodeAsync(Guid userId, CancellationToken cancellationToken);
}
