namespace wa.infrastructure.Persistence;

/// <summary>T-006-03 (FR-006-5): Unsubscribe result — Success or NotFound.</summary>
/// <param name="Success">True when the token was valid and the address is now suppressed (or already was); false when the token is invalid.</param>
public record UnsubscribeResult(bool Success);
