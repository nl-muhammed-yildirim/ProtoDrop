using Microsoft.Extensions.Logging;
using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Correlation;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07: the write result — email captured PRE-delete so the endpoint can audit {email} after the row is soft-deleted
/// (IAppUserStore.GetByIdAsync filters DeletedAtUtc == null, so the email is unreadable post-delete).
/// </summary>
/// <param name="UserId">Identifier of the user of the admin force delete result.</param>
/// <param name="Email">The email of the admin force delete result.</param>
public sealed record AdminForceDeleteResult(Guid UserId, string Email);
