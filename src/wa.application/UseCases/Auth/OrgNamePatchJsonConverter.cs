using System.Text.Json;
using System.Text.Json.Serialization;

namespace wa.application.UseCases.Auth;

/// <summary>Reads the org name PATCH field; HandleNull makes an explicit JSON null reach Read so it can be told apart from absence.</summary>
public sealed class OrgNamePatchJsonConverter : JsonConverter<OrgNamePatch>
{
    /// <summary>True so that an explicit JSON <c>null</c> reaches <see cref="Read"/> instead of being skipped.</summary>
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override OrgNamePatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => OrgNamePatch.Of(null),
            JsonTokenType.String => OrgNamePatch.Of(reader.GetString()),
            _ => throw new JsonException("orgName must be a string or null.")
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, OrgNamePatch value, JsonSerializerOptions options)
    {
        if (value.IsSet && value.Value is not null)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
