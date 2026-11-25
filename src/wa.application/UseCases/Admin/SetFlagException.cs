using wa.application.Limits;
using wa.application.Ports;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Admin;

/// <summary>
/// T-011-08 / US-044-01: the single use-case exception for the set-flag write — house style: private ctor + static factories.
/// ForInvalid → 400 FLAG_INVALID (value not valid for the key's type; old value stays live, no audit row).
/// ForUnknown → 404 FLAG_UNKNOWN (key is not a seeded flag). Both codes are in the closed TA-4.1.3 list (US-044-01).
/// </summary>
public sealed class SetFlagException : Exception
{
    private SetFlagException(string code, string message) : base(message) => Code = code;

    /// <summary>The closed TA-4.1.3 problem code (<c>FLAG_INVALID</c> or <c>FLAG_UNKNOWN</c>); the endpoint maps it to its canonical status and title.</summary>
    public string Code { get; }

    /// <summary>Value fails the key's typed rules → 400 FLAG_INVALID (EC-044-1, EC-044-5).</summary>
    /// <returns>An exception carrying the <c>FLAG_INVALID</c> code.</returns>
    public static SetFlagException ForInvalid() =>
        new(ProblemCodes.FlagInvalid, ProblemCodes.TitleOf(ProblemCodes.FlagInvalid));

    /// <summary>No seeded row for the key → 404 FLAG_UNKNOWN (AC-044-3).</summary>
    /// <returns>An exception carrying the <c>FLAG_UNKNOWN</c> code.</returns>
    public static SetFlagException ForUnknown() =>
        new(ProblemCodes.FlagUnknown, ProblemCodes.TitleOf(ProblemCodes.FlagUnknown));
}
