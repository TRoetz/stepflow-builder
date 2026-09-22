using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    /// <summary>
    /// Maps JSON null on JToken-typed properties to C# null (and vice versa).
    /// Without this, an absent property round-trips through a checkpoint file as an explicit
    /// JSON null and comes back as JValue(null) — which fails every `== null` check in the engine
    /// (e.g. ApplyParameters treating "no Parameters" as a template that evaluates to null).
    /// Nested JSON nulls inside a token's content are preserved; only the property value itself is normalized.
    /// </summary>
    public class NullableJTokenConverter : JsonConverter<JToken>
    {
        public override JToken? ReadJson(JsonReader reader, Type objectType, JToken? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var token = JToken.Load(reader);
            return token.Type == JTokenType.Null ? null : token;
        }

        public override void WriteJson(JsonWriter writer, JToken? value, JsonSerializer serializer)
        {
            // Keep the on-disk shape identical to before (explicit null), so existing checkpoint files need no migration.
            if (value is null || value.Type == JTokenType.Null)
                writer.WriteNull();
            else
                value.WriteTo(writer);
        }
    }
}
