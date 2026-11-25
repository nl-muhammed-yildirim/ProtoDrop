namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-04 (D-4): redeem result — same profile DTO shape as LoginResult + session token value.
/// The wire shape is pinned at the endpoint's single construction site (TA-4.2a thin-endpoint rule).
/// </summary>
/// <param name="UserId">Primary key of the signed-in user.</param>
/// <param name="Email">Sign-in email; PII (TA-9.4).</param>
/// <param name="DisplayName">Name shown to recipients as the sender.</param>
/// <param name="Theme">Stored UI theme preference.</param>
/// <param name="PlanCode">Code of the user's current plan.</param>
/// <param name="SessionToken">Signed 30-day session token; the endpoint also sets it as the session cookie.</param>
public sealed record RedeemMagicLinkResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string SessionToken);
