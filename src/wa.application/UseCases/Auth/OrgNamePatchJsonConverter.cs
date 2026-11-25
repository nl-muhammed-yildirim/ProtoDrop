using System.Text.Json;
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

/// <summary>Reads the org name PATCH field; HandleNull makes an explicit JSON null reach Read so it can be told apart from absence.</summary>
public sealed class OrgNamePatchJsonConverter : JsonConverter<OrgNamePatch>
{
    public override bool HandleNull => true;

    public override OrgNamePatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => OrgNamePatch.Of(null),
            JsonTokenType.String => OrgNamePatch.Of(reader.GetString()),
            _ => throw new JsonException("orgName must be a string or null.")
        };

    public override void Write(Utf8JsonWriter writer, OrgNamePatch value, JsonSerializerOptions options)
    {
        if (value.IsSet && value.Value is not null)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
