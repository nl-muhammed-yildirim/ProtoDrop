using wa.application.Ports;
using wa.application.UseCases.Auth;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-07 (D-2): the single use-case exception for the set-plan write — house style: private ctor + static factories.
/// Both factories map to 404 NOT_FOUND (unknown user OR unknown planId — Decision 2). No new closed-list member (TA-4.1.3 stays at 18).
/// </summary>
public sealed class SetUserPlanException : Exception
{
    private SetUserPlanException(string message) : base(message) { }

    /// <summary>No row for the user id (unknown OR soft-deleted) — endpoint → 404 NOT_FOUND.</summary>
    /// <returns>The set user plan exception.</returns>
    public static SetUserPlanException ForNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound)); // title from the closed table — never "user not found"

    /// <summary>No row for the planId — endpoint → 404 NOT_FOUND (Decision 2: id resolves to no row = NOT_FOUND, not 400).</summary>
    /// <returns>The set user plan exception.</returns>
    public static SetUserPlanException ForPlanNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound));
}
