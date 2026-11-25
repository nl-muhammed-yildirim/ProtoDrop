using System.Text.Json.Serialization;

namespace wa.application.UseCases.Auth;

/// <summary>
/// F-PRF-001 (FR-021-1 / TA-4.2#13): tri-state PATCH field. Absent from the body = <c>default</c> (<see cref="IsSet"/> false, unchanged);
/// explicit null or a blank string = clear; a string = the (trimmed) value. Needed because a plain string? cannot tell absent from null.
/// </summary>
/// <param name="IsSet">True when the field was present in the PATCH body (even as null).</param>
/// <param name="Value">The submitted value; null or blank clears the organization name.</param>
[JsonConverter(typeof(OrgNamePatchJsonConverter))]
public readonly record struct OrgNamePatch(bool IsSet, string? Value)
{
    /// <summary>Creates a patch that marks the organization name as present in the request.</summary>
    /// <param name="value">The submitted value; null or blank means clear.</param>
    /// <returns>A patch marked as set that carries <paramref name="value"/>.</returns>
    public static OrgNamePatch Of(string? value) => new(true, value);
}
