using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Errors;

namespace wa.application.UseCases.Auth;

/// <summary>
/// T-008-06 (D-2): the single use-case exception for the /auth/me endpoints — house style: private ctor + static factories.
/// NOT_FOUND maps to ProblemCodes.NotFound (404); VALIDATION-family maps to ProblemCodes.Validation (400) via BusinessException
/// at the endpoint. No new closed-list member is added anywhere (TA-4.1.3 stays at 18).
/// </summary>
public sealed class MeProfileException : Exception
{
    private MeProfileException(string message) : base(message) { }

    /// <summary>The request field a validation failure belongs to (TA-4.1.3 errors[].field); null when not field-specific.</summary>
    public string? Field { get; private init; }

    /// <summary>No row for the session user id (unknown OR soft-deleted) — endpoint 13 → 404 NOT_FOUND (spec's literal pin).</summary>
    public static MeProfileException ForNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound)); // title from the closed table — never "user not found"

    /// <summary>PATCH theme is not one of system/light/dark → 400 VALIDATION.</summary>
    public static MeProfileException ForInvalidTheme() =>
        new("theme must be one of: system, light, dark.");

    /// <summary>PATCH displayName exceeds the NVARCHAR(100) column after trimming → 400 VALIDATION.</summary>
    public static MeProfileException ForDisplayNameTooLong(string maxLength) =>
        new($"displayName must be at most {maxLength} characters after trimming.");

    /// <summary>PATCH with an all-null body → 400 VALIDATION (an empty PATCH is a silent no-op; reject it).</summary>
    public static MeProfileException ForEmptyPatch() =>
        new("Provide at least one of displayName, theme, orgName or emailNotifications.");

    /// <summary>PATCH orgName longer than ORG_NAME_MAX_LENGTH (after trimming) or containing a line break → 400 VALIDATION (field orgName).</summary>
    public static MeProfileException ForInvalidOrgName() =>
        new($"orgName must be at most {AppUser.OrgNameMaxLength} characters and contain no line breaks.") { Field = "orgName" };
}
