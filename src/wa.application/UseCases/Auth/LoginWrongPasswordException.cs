using MediatR;
using Microsoft.AspNetCore.Identity;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-02: the single use-case exception for row-vs-hash failures — every auth failure funnels through it so the wire shape
/// never leaks which emails exist (no "user not found"; don't leak which emails exist). The endpoint maps it to
/// BusinessException(ProblemCodes.WrongPassword, ...) → 401 WRONG_PASSWORD (TA-4.1.3 closed list — already in the table; no new code).
/// </summary>
public sealed class LoginWrongPasswordException : Exception
{
    private LoginWrongPasswordException(string message) : base(message) { }

    public static LoginWrongPasswordException ForWrongPassword() =>
        new(ProblemCodes.TitleOf(ProblemCodes.WrongPassword)); // title from the closed table — never "user not found"
}
