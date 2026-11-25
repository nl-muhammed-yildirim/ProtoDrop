using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>
/// F-PRF-001 (FR-021-1 / TA-4.2#13): tri-state PATCH field. Absent from the body = <c>default</c> (<see cref="IsSet"/> false, unchanged);
/// explicit null or a blank string = clear; a string = the (trimmed) value. Needed because a plain string? cannot tell absent from null.
/// </summary>
[JsonConverter(typeof(OrgNamePatchJsonConverter))]
public readonly record struct OrgNamePatch(bool IsSet, string? Value)
{
    public static OrgNamePatch Of(string? value) => new(true, value);
}
