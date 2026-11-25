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
public sealed record AdminForceDeleteResult(Guid UserId, string Email);
