using wa.application.Limits;
using wa.application.UseCases.Auth;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>
/// A guest-row re-send attempt (EC-010-4). The endpoint catches this and throws
/// BusinessException(ProblemCodes.Forbidden) — keeps the closed code list intact.
/// </summary>
public sealed class ResendForbiddenException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ResendForbiddenException"/> class.</summary>
    public ResendForbiddenException() : base("This transfer has no owner to re-send from.") { }
}
