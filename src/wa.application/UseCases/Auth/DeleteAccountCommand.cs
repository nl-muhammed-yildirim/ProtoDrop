using MediatR;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers + account_deleted.</summary>
/// <param name="UserId">Id of the signed-in user who deletes their own account.</param>
public sealed record DeleteAccountCommand(Guid UserId) : IRequest<DeleteAccountResult>;
